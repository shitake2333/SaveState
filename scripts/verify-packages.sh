#!/usr/bin/env bash
#
# Verifies the packages as an OUTSIDE consumer sees them:
#   1. clear the NuGet cache for SaveState (otherwise the previous build of the same version wins),
#   2. pack the solution into ./artifacts,
#   3. create a throwaway project in a temp directory that only uses PackageReference entries
#      (Shared + Runtime + Policy + DependencyInjection + Generator),
#   4. build and run it: the generator must run from the package, the container wiring and the
#      write-policy helpers must work, and a correct save file must come out.
#
# This is the check that would have caught "the analyzer is not inside the nupkg" or
# "the package dependency graph does not resolve" before publishing.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

echo "==> packing into artifacts/"
rm -rf artifacts
dotnet pack SaveState.sln -c Release -o artifacts --nologo > /dev/null

echo "==> clearing the NuGet cache for SaveState.* (same-version re-pack safety)"
for id in shared runtime generator policy dependencyinjection godot; do
  rm -rf "$HOME/.nuget/packages/savestate.$id"
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cd "$work"

# NuGet (a native Windows tool) needs a Windows path; Git Bash/MSYS reports /d/... otherwise.
artifacts_dir="$(cd "$repo_root/artifacts" && pwd -W 2>/dev/null || echo "$repo_root/artifacts")"

echo "==> creating a throwaway consumer in $work (feed: $artifacts_dir)"
cat > nuget.config <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="savestate-local" value="$artifacts_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

cat > Consumer.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SaveState.Shared" Version="0.1.0-alpha.1" />
    <PackageReference Include="SaveState.Runtime" Version="0.1.0-alpha.1" />
    <PackageReference Include="SaveState.Policy" Version="0.1.0-alpha.1" />
    <PackageReference Include="SaveState.DependencyInjection" Version="0.1.0-alpha.1" />
    <PackageReference Include="SaveState.Generator" Version="0.1.0-alpha.1" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.10" />
  </ItemGroup>
</Project>
EOF

cat > Program.cs <<'EOF'
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using SaveState;
using SaveState.DependencyInjection;
using SaveState.Policy;

public sealed class Flags : StateBase
{
    public int Count { get; set; }
    public HashSet<int> Seen { get; set; } = new HashSet<int>();
}

[SaveService]
public partial class FlagsService
{
    [SavedState(File = "flags.json", Key = "Flags")]
    public Flags State { get; private set; } = new Flags();
}

[SaveFile("flags.json")]
public sealed partial class FlagsSaveFile : SaveFileDefinition
{
}

public static class Program
{
    public static int Main()
    {
        var store = new InMemoryStore();
        var services = new ServiceCollection();
        services.AddSaveService<FlagsService>();
        services.AddSaveState(options =>
        {
            options.Store = store;
            options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);
        });

        using var provider = services.BuildServiceProvider();
        var session = provider.GetSaveFileSession<FlagsSaveFile>();

        if (!session.EnsureCreated())
        {
            Console.Error.WriteLine("FAIL: EnsureCreated() did not create the file");
            return 1;
        }

        var gate = new SaveGate(() => false);
        if (gate.TrySave(session.Save))
        {
            Console.Error.WriteLine("FAIL: a closed gate must not write");
            return 1;
        }

        using (var scheduler = new SaveScheduler(session.Save, null, TimeSpan.FromMilliseconds(20)))
        {
            scheduler.RequestSave();
            scheduler.RequestSave();
            scheduler.Flush();
            if (scheduler.SaveCount != 1)
            {
                Console.Error.WriteLine("FAIL: two requests should collapse into one write");
                return 1;
            }
        }

        provider.GetRequiredService<FlagsService>().State.Count = 9;
        session.Save();

        string json = store.Read("flags.json");
        if (json.IndexOf("\"Count\": 9", StringComparison.Ordinal) < 0)
        {
            Console.Error.WriteLine("FAIL: the saved file does not contain the new state:\n" + json);
            return 1;
        }

        Console.WriteLine("OK - packages work for an outside consumer");
        return 0;
    }
}
EOF

dotnet run -c Release
