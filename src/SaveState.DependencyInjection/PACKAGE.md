# SaveState.DependencyInjection

`Microsoft.Extensions.DependencyInjection` integration for [SaveState](https://github.com/shitake2333/SaveState).

```bash
dotnet add package SaveState.DependencyInjection
```

```csharp
services.AddSaveService<PlayerProfile>();          // your services, registered normally

services.AddSaveState(options =>
{
    options.Store  = new FileSystemStore(saveDirectory);   // required
    options.Logger = logger;                               // optional
    // The generator emits this next to your services: no reflection, no forgotten service.
    options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);
});

var session = provider.GetSaveFileSession<ProfileSaveFile>();
```

| Member | Meaning |
|---|---|
| `SaveStateOptions.Store` | `ISaveStore`; a missing store throws `SaveStateException` at startup |
| `SaveStateOptions.Logger` | `ISaveLogger` (defaults to a no-op logger) |
| `SaveStateOptions.Migrations` | migrations handed to every session |
| `SaveStateOptions.JsonOptions` | optional JSON policy override |
| `SaveStateOptions.RegisterServices` | `(SaveRegistry, Func<Type, object>)` - fills the registry from the container |
| `AddSaveService<TService>()` | registers a save service as a singleton |
| `GetSaveFileSession<TFile>()` | builds a session from the container |

`RegisterServices` is a delegate on purpose: the library stays container-agnostic (it never references a container
or scans assemblies), while the generated `AddGeneratedServices` keeps the registry in sync with the container.

See [Adapters](https://github.com/shitake2333/SaveState/blob/main/docs/adapters.md) and the
[API reference](https://github.com/shitake2333/SaveState/blob/main/docs/api.md).

MIT licensed.
