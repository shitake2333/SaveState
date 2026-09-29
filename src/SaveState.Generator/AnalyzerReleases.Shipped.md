## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SAV001 | SaveState | Error | `[SaveFile]` class must be partial
SAV002 | SaveState | Error | `[SaveFile]` class must derive from SaveFileDefinition
SAV003 | SaveState | Error | `[SaveFile]` class needs a public parameterless constructor
SAV004 | SaveState | Error | save file name must be a plain file name
SAV005 | SaveState | Error | save files and services must be top-level, non-generic types
SAV006 | SaveState | Error | the save file of a `[SavedState]` member cannot be determined
SAV007 | SaveState | Error | section key must be a valid C# identifier
SAV008 | SaveState | Error | section key is duplicated inside one save file
SAV009 | SaveState | Error | `[SavedState]` member is not writable (read-only field / no setter)
SAV010 | SaveState | Error | section type cannot be instantiated (default instance needed for a missing section)
SAV011 | SaveState | Warning | save file has no `[SavedState]` sections
SAV012 | SaveState | Error | `[SaveService]` class must be partial
SAV013 | SaveState | Error | `[SavedState]` member whose class is not marked `[SaveService]`
