# Time Tracker 2K

A small Windows tray app that tracks local work-session time and keeps local history for up to two years.

## Features

- Runs in the Windows notification area.
- Opens from the tray icon with one left click.
- Shows weekly and daily summaries.
- Weekly summaries are the main working-time view.
- Weekly summaries run from Monday through Sunday and display the Monday date.
- The daily view shows the last 30 days as a sanity check.
- Tracks on time, logged-in time, away time, and logged-out time.
- Shows today's first login time with minutes in the overview.
- Away time starts after 5 minutes without keyboard or mouse input.
- Lock/logoff time is counted separately from logged-in time.
- Weekly view supports quick weekly corrections for vacation or personal-time credit.
- Corrections can be entered with `- Week`, `- Day`, `+ Day`, `+ Week`, or a manual hour value.
- Negative correction accounts for logged in while not working.
- Positive correction records time off or holidays.
- Standard weekly worktime defaults to `37,5` hours and is configured in the Settings tab.
- Weekly overtime tolerance defaults to `10%` and is configured in the Settings tab; overtime within the tolerance is not counted.
- Cumulative overtime is calculated from logged-in time after corrections.
- Hourly rate and currency are configured in the Settings tab. The hourly rate defaults to `20`; EUR is the default currency.
- Weekly summaries show the worth of counted overtime based on the configured hourly rate.
- Backup folder and backup interval are configured in the Settings tab. The interval defaults to daily.
- By default, backups are stored locally under `%LOCALAPPDATA%\TimeTracker2K\Backups`.
- Saved backups can be restored from the Settings tab. The current data file is copied to a safety file before restore.
- Values are shown as decimal hours with comma formatting, for example `1,5` for 1 hour 30 minutes.
- Copy the active table to Excel with `Copy Excel` or `Ctrl+C`. The weekly table copies all retained weekly data.
- Double-click a table cell to copy just that value.
- The dashboard refreshes automatically every 10 seconds while it is the foreground window.
- Optional `Start with Windows` tray menu item.

## Privacy and transparency

- The app does not track keystrokes.
- The app does not take screenshots.
- The app does not track websites.
- The app does not track which apps you use.
- The app does not upload data.
- The tray icon stays visible while the app is running. Exiting the tray app stops tracking.

## Data

The app stores local data at:

```text
%LOCALAPPDATA%\TimeTracker2K\work-log.json
```

The configurable backup writes one local backup file named:

```text
work-log-backup.json
```

No server or network connection is required for tracking.

## Reliability notes

- The app checkpoints time every 30 seconds.
- The dashboard refreshes every 10 seconds only while it is the active foreground window.
- Lock/logoff intervals are checkpointed while they are happening, instead of relying on one large calculation at unlock.
- Logoff recovery after restart subtracts sleep and shutdown spans where Windows event logs are available.
- The JSON data file is written atomically with a backup file to reduce the risk of corruption during shutdown or crashes.
- The configurable backup is refreshed daily by default, or weekly if selected in Settings.
- Sleep/suspend pauses tracking and resumes with the previous tracking mode.

## Build

This repository pins the .NET SDK in `global.json`:

```text
9.0.203
```

Build and publish with:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

The published executable is written to:

```text
bin\Release\net9.0-windows\win-x64\publish\TimeTracker2K.exe
```

The project publishes a self-contained single-file `win-x64` executable, so the target machine does not need a separate .NET 9 runtime install.

## Setup

Download the latest version 1 setup from:

```text
https://github.com/frsksx/Time-Tracker-2K/releases/download/v1.0.0/TimeTracker2KSetup.exe
```

Version `v1.0.0` setup SHA-256:

```text
FFF40BC7506436FD1F6551C656CCF5C702DF518528F1D43138E75B1AEFF6C295
```

Create the per-user setup executable with:

```powershell
.\installer\build-setup.cmd
```

The setup executable is written to:

```text
installer\out\TimeTracker2KSetup.exe
```

The setup installs the app to `%LOCALAPPDATA%\Programs\TimeTracker2K`, creates Start Menu shortcuts, keeps existing tracking data, and preserves the Start with Windows setting if it was already enabled.

To compare a recreated executable:

```powershell
Get-FileHash .\bin\Release\net9.0-windows\win-x64\publish\TimeTracker2K.exe -Algorithm SHA256
```

Matching the same SHA-256 requires the same source files, SDK `9.0.203`, runtime packs, operating system/toolchain behavior, and publish command.

On first run after upgrading from the older `LoginDurationTracker` build, existing local data is copied forward from the old app-data folder if the new data file does not exist yet.

## Repository

https://github.com/frsksx/Time-Tracker-2K

## License

MIT
