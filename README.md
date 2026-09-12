# Knights Cleaner
A small, open-source Windows cleaner built with C# and WPF on .NET 10.

## Version 0.1
- One dark window with a large live Activity Log, service cards, Select All and Run.
- Automatically lists drives and marks the Windows system drive.
- Only the system drive is selected initially: external drives (including USB disks reported as Fixed) and all other drives remain unchecked.
- Only User Temp (`LocalApplicationData/Temp`) and Windows Temp (`Windows/Temp`) are supported.
- Drive selection gates the two known temp locations; it never searches a drive for arbitrary Temp folders.
- Skips files modified in the last 24 hours, inaccessible/locked files, reparse points and directories with reparse-point ancestors.
- Never elevates privileges, changes permissions, forces deletion, or removes the temp root.
- No registry cleaning, service changes or tweaks.

Deletion is permanent. Close applications before cleaning. Cancel stops before the next file; it cannot undo completed deletions. The log shows each operation and totals. Byte totals are logical file sizes, not exact disk space reclaimed. The UI retains the latest 2,000 log entries.

## Build and run
Requires Windows and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
```powershell
dotnet build src/KnightsCleaner.App/KnightsCleaner.App.csproj -c Release
dotnet run --project src/KnightsCleaner.App
dotnet run --project tests/KnightsCleaner.Tests -c Release
dotnet publish src/KnightsCleaner.App -c Release -r win-x64 --self-contained true -o publish
```
Run `publish/KnightsCleaner.exe`. Standard permissions are the default; Windows Temp entries without permission are skipped.

## Structure
- `src/KnightsCleaner.Core`: cleanup policy and engine, independent of WPF.
- `src/KnightsCleaner.App`: WPF window and drive discovery.
- `tests/KnightsCleaner.Tests`: dependency-free safety regression harness using isolated fixtures.
- `.github/workflows/build.yml`: Windows build, safety tests and downloadable app artifact.

User Temp deliberately uses the conventional per-user location rather than trusting an arbitrary TEMP environment variable. Redirected custom TEMP paths are not cleaned in v0.1. Other drives have no cleanup targets unless one of the two known folders resides there. Refresh drives after connecting or disconnecting storage.

## License
MIT. Copyright (c) 2026 Knights Cleaner contributors.
