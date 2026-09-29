# SaveState.Generator

The incremental source generator of [SaveState](https://github.com/shitake2333/SaveState). It ships as an
**analyzer** package: reference it and the partial classes you declare get their implementation at compile time.

```bash
dotnet add package SaveState.Generator
```

For a library that does not want to expose the analyzer to its own consumers, add `PrivateAssets="all"`.

## What it generates

For every `[SaveFile("profile.json")] partial class … : SaveFileDefinition`:

* `FileNameValue`, `GetFileName()`, `Version`
* one property per `[SavedState]` key, and
* `CollectFrom` (service → DTO), `RestoreTo` (DTO → service, null-checking every section, then calling the
  service's `OnAfterRestore`) and `FillMissingDefaults` (a missing section becomes a default instance, never `null`).

For every `[SaveService] partial class`: an `internal` accessor per saved member (so it can stay `private set`)
and the optional `partial void OnAfterRestore()` hook.

It also emits `SaveServiceRegistration.AddGeneratedServices(this SaveRegistry, Func<Type, object>)` - one call that
registers every service of the assembly through your resolver (used by
[`SaveState.DependencyInjection`](https://www.nuget.org/packages/SaveState.DependencyInjection)).

## Compile-time diagnostics

`SAV001`–`SAV013` catch, among others: a class that is not `partial`, a member with no setter, a section type that
cannot be instantiated, duplicate or invalid section keys, and ambiguous file ownership. The full table is in the
[API reference](https://github.com/shitake2333/SaveState/blob/main/docs/api.md#diagnostics).

Requires `SaveState.Shared`; see
[Getting started](https://github.com/shitake2333/SaveState/blob/main/docs/getting-started.md).

MIT licensed.
