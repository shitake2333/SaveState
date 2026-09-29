using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using SaveState;
using SaveState.DependencyInjection;
using Xunit;

namespace SaveState.DependencyInjection.Tests;

// ── A minimal host model: one state, one service, one save file ───────────────────────────────

public sealed class ProgressState : StateBase
{
    public int Level { get; set; } = 1;

    public HashSet<string> Seen { get; set; } = new HashSet<string>();
}

[SaveService]
public partial class ProgressService
{
    [SavedState(File = "progress.json", Key = "Progress")]
    public ProgressState State { get; private set; } = new ProgressState();
}

[SaveFile("progress.json")]
public sealed partial class ProgressSaveFile : SaveFileDefinition
{
}

public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(InMemoryStore store, bool registerGeneratedServices = true)
    {
        var services = new ServiceCollection();
        services.AddSaveService<ProgressService>();

        services.AddSaveState(options =>
        {
            options.Store = store;
            if (registerGeneratedServices)
            {
                options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);
            }
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSaveState_WithoutAStore_FailsFast()
    {
        var services = new ServiceCollection();

        SaveStateException error = Assert.Throws<SaveStateException>(() => services.AddSaveState(options => { }));
        Assert.Contains("Store", error.Message);
    }

    [Fact]
    public void AddSaveState_RegistersTheFrameworkPieces()
    {
        var store = new InMemoryStore();

        using (ServiceProvider provider = BuildProvider(store))
        {
            Assert.Same(store, provider.GetRequiredService<ISaveStore>());
            Assert.NotNull(provider.GetRequiredService<ISaveLogger>());
            Assert.Same(provider.GetRequiredService<SaveRegistry>(), provider.GetRequiredService<SaveRegistry>());
        }
    }

    [Fact]
    public void GeneratedRegistration_FillsTheRegistryFromTheContainer()
    {
        using (ServiceProvider provider = BuildProvider(new InMemoryStore()))
        {
            ProgressService service = provider.GetRequiredService<SaveRegistry>().Get<ProgressService>();

            Assert.Same(provider.GetRequiredService<ProgressService>(), service);
        }
    }

    [Fact]
    public void MissingGeneratedRegistration_ReportsTheUnregisteredService()
    {
        using (ServiceProvider provider = BuildProvider(new InMemoryStore(), registerGeneratedServices: false))
        {
            // The container builds fine; the failure surfaces when save data is actually needed
            // (and it names the type, so the missing registration is obvious).
            SaveFileSession<ProgressSaveFile> session = provider.GetSaveFileSession<ProgressSaveFile>();
            Assert.Throws<SaveStateException>(() => session.Save() /* CollectFrom touches the registry */);
        }
    }

    [Fact]
    public void GetSaveFileSession_EnsuresCreatesAndSaves()
    {
        var store = new InMemoryStore();

        using (ServiceProvider provider = BuildProvider(store))
        {
            SaveFileSession<ProgressSaveFile> session = provider.GetSaveFileSession<ProgressSaveFile>();

            Assert.True(session.EnsureCreated());
            Assert.True(store.Exists("progress.json"));

            ProgressService service = provider.GetRequiredService<ProgressService>();
            service.State.Level = 4;
            service.State.Seen.Add("boss");

            Assert.True(session.Save());

            string json = store.Read("progress.json");
            Assert.Contains("\"Level\": 4", json);
            Assert.Contains("boss", json);
            Assert.Contains("\"Version\": 1", json);
        }
    }

    [Fact]
    public void AddSaveService_RegistersASingleton()
    {
        var services = new ServiceCollection();
        services.AddSaveService<ProgressService>();

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            Assert.Same(
                provider.GetRequiredService<ProgressService>(),
                provider.GetRequiredService<ProgressService>());
        }
    }
}
