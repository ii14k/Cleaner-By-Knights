# Knights Cleaner
A native Windows application converted from the supplied **Start_up_Cleaner.bat**, built with C# / WPF and .NET 10.

## Download
Open [Releases](https://github.com/ii14k/Cleaner-By-Knights/releases) and download **KnightsCleaner-win-x64.zip**. Extract the entire archive and run **KnightsCleaner.exe**. The ZIP includes the .NET runtime.
Builds are also available under Actions → successful run → Artifacts.

## Script functions in the app
| Option | Behavior |
|---|---|
| User Temp | Cleans files in the current account's TEMP folder, including nested files |
| Windows Temp | Cleans files in the actual Windows Temp folder |
| Prefetch | Cleans top-level Prefetch files; Windows rebuilds this launch cache |
| Event Logs | Enumerates logs with wevtutil and clears each selected operation's log list, reporting failures |
| Windows Update | Cleans SoftwareDistribution contents after stopping wuauserv, UsoSvc and BITS, then restores services that were running |

The app does not run automatically at startup. The original script's filename does not install a startup task.

## Usage
1. Select drives. The system drive is detected automatically; all other drives, including external USB disks, start unchecked.
2. Select services. Only User Temp and Windows Temp start checked.
3. Use **Administrator** to relaunch with a Windows permission prompt when Prefetch, Event Logs or Windows Update is needed. Reselect operations after relaunch.
4. Click **Run Cleaner**. Advanced operations show a confirmation with their effects.
5. Follow DELETE, SKIP, FAILED and RESTORE entries in the live console. Cancel waits for service restoration before returning.

Select All selects all five operations, including advanced ones; their confirmation still applies. Drive selection only gates known folders (and the system drive for Event Logs). No arbitrary drive-wide Temp search is performed.

## Differences and corrections from the BAT
- Fixes the PowerShell foreach syntax embedded in CMD: event logs are enumerated and cleared using separate native wevtutil calls with checked exit codes.
- Uses the actual Windows directory instead of hard-coded C:\\Windows.
- Never deletes/recreates the Temp or SoftwareDistribution root, preserving its permissions. Empty subdirectories are retained.
- Skips locked, inaccessible, read-only files and reparse points rather than forcing deletion or changing permissions.
- Processes eligible files regardless of age to match the supplied script; v0.1's 24-hour filter no longer applies to app operations.
- Refuses a custom TEMP path unless its final folder is Temp or tmp, protecting against a broad redirected environment variable.
- Restores services that were originally running, including after an error or cancellation. Services originally stopped remain stopped. Restoration failures are prominent in the log and require a Windows restart.
- Does not stop dependent unrelated services or change startup types.
- Event Logs and Windows Update are off initially. There is no registry cleaner, task killer or permanent service disabling.

Deletion and log clearing are permanent. Clearing Prefetch may slow subsequent launches. Clearing Event Logs removes diagnostic history. Resetting SoftwareDistribution removes cached downloads/local update history and may require Windows to download updates again; it does not uninstall installed updates. Do not run the update reset during an update installation. Stop failures abort the reset before file deletion.

This is not a security boundary against software concurrently replacing paths. Only use it for ordinary local maintenance. Do not terminate the process during service restoration.

## Build and tests
Windows and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) required:
```powershell
dotnet build KnightsCleaner.slnx -c Release
dotnet run --project tests/KnightsCleaner.Tests -c Release
dotnet run --project tests/KnightsCleaner.UiTests -c Release
dotnet publish src/KnightsCleaner.App -c Release -r win-x64 --self-contained true -o publish
```

Core tests use isolated fixture files and fake services. UI tests open the window and verify defaults without running any cleanup. Real event log deletion and Windows Update resets are not executed in automated tests.

## Structure
- src/KnightsCleaner.Core: file cleanup and service restoration transaction
- src/KnightsCleaner.App: dark WPF window and Windows-specific script operations
- tests: isolated engine, service lifecycle and UI startup checks
- .github/workflows/build.yml: Windows build, tests, screenshot and application packages

MIT License. Copyright (c) 2026 Knights Cleaner contributors.
