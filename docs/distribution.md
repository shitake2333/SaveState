# Building, packing and publishing

## Build and test

```bash
dotnet build SaveState.sln -c Debug
dotnet test  SaveState.sln -c Debug     # 91 cases: contracts, stores, migrations, generator, container
dotnet pack  SaveState.sln -c Release -o artifacts
```

The generator is packed as an analyzer package, so `dotnet pack` produces:

| Package | Notes |
|---|---|
| `SaveState.Shared` | dual-targeted (`netstandard2.0`, `net10.0`) |
| `SaveState.Runtime` | dual-targeted; references `System.Text.Json` only for the `netstandard2.0` target |
| `SaveState.Generator` | contains `analyzers/dotnet/cs/SaveState.Generator.dll` + the two `AnalyzerReleases.*.md` files |
| `SaveState.Policy` | optional |
| `SaveState.Godot` | `net10.0`, built with `Godot.NET.Sdk`, depends on `GodotSharp` |
| `SaveState.DependencyInjection` | dual-targeted |

Versions live in one place: `build/Version.props`. The generator and the runtime move in lockstep - always ship
them with the same version, because the generated code and the runtime types are two halves of one contract.

## Consuming a local build

```xml
<!-- nuget.config next to the consuming project -->
<configuration>
  <packageSources>
    <clear />
    <add key="savestate-local" value="D:\path\to\local-nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

```bash
dotnet pack SaveState.sln -c Release -o D:\path\to\local-nuget
```

In the consumer:

```xml
<PackageReference Include="SaveState.Shared" Version="0.1.0-alpha.1" />
<PackageReference Include="SaveState.Runtime" Version="0.1.0-alpha.1" />
<PackageReference Include="SaveState.Godot" Version="0.1.0-alpha.1" />
<PackageReference Include="SaveState.Generator" Version="0.1.0-alpha.1" PrivateAssets="all" />
```

> **Re-packing the same version?** NuGet caches a package by id + version in the global packages folder
> (`~/.nuget/packages/savestate.*`). If you `dotnet pack` a new build under an unchanged version, the consumer keeps
> resolving the *old* package and nothing appears to change. Either bump `build/Version.props` or delete that folder:

```bash
rm -rf ~/.nuget/packages/savestate.shared ~/.nuget/packages/savestate.runtime \
       ~/.nuget/packages/savestate.generator ~/.nuget/packages/savestate.policy \
       ~/.nuget/packages/savestate.godot ~/.nuget/packages/savestate.dependencyinjection
```

`scripts/verify-packages.sh` does exactly that, then packs and consumes the packages from a throwaway project - it is
the check that catches "the analyzer is not inside the nupkg" or "the dependency graph does not resolve":

```bash
bash scripts/verify-packages.sh
# ==> packing into artifacts/
# ==> clearing the NuGet cache for SaveState.*
# ==> creating a throwaway consumer in /tmp/…
# OK - packages work for an outside consumer
```

## Publishing to nuget.org

1. Bump `build/Version.props` (and, if analyzer rules changed, move them from
   `AnalyzerReleases.Unshipped.md` into `AnalyzerReleases.Shipped.md` under the new release heading).
2. `dotnet test SaveState.sln -c Release` and `bash scripts/verify-packages.sh`.
3. `dotnet pack SaveState.sln -c Release -o artifacts` and push each `SaveState.*.nupkg`.
4. Tag the commit with the version.

`.github/workflows/ci.yml` runs restore → build → test → pack on every push and uploads the packages as a build
artifact, so a release is only the push step.

## Version policy

* **SemVer.** Prerelease builds use `-alpha.N` / `-beta.N` (as in `build/Version.props`).
* The **generated member names** documented in [api.md](api.md#generated-members) are part of the public contract:
  changing them is a breaking change for every consumer.
* The **diagnostic IDs** `SAV001`–`SAV013` are stable; a removed rule stays in the release notes and is never reused
  for something else.
* Runtime and generator ship together: a mismatched pair (older generated code with a newer runtime) is not
  supported, which is why `SaveState.Shared`/`Runtime`/`Generator` should always be bumped as a set.

## Repository layout

```
SaveState.sln
build/Version.props                 one version number for every package
src/SaveState.Shared/               contracts (zero dependencies)
src/SaveState.Generator/            generator + AnalyzerReleases.*.md
src/SaveState.Runtime/              sessions, stores, JSON, gate, migrations
src/SaveState.Policy/               SaveScheduler, AsyncSaveScheduler
src/SaveState.Godot/                Godot adapter
src/SaveState.DependencyInjection/  container integration
tests/SaveState.Tests/              contracts, migrations, generator, policy
tests/SaveState.DependencyInjection.Tests/
samples/SaveState.Samples.Console/  runnable end-to-end example
scripts/verify-packages.sh          "does an outside consumer work?" check
docs/                               these documents
```
