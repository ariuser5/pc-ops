# Activity Watchdog

Small console app that shows how long it has been since you last reset a machine-specific activity timer.

## Run

```powershell
dotnet run --project ./dotnet/activity-watchdog/src/ActivityWatchdog.csproj
```

## Controls

- `R`: reset and restart the timer.
- `S`: stop the timer. There is no resume; resetting starts it again.
- `Q`: quit the app.

## Config

The app reads `appsettings.json` from the executable folder by default. You can point it somewhere else with `--config`.

```powershell
dotnet run --project ./dotnet/activity-watchdog/src/ActivityWatchdog.csproj -- --config C:\path\to\appsettings.json
```

Threshold commands are optional and run once per threshold until the timer is reset. The spawned process receives these environment variables:

- `ACTIVITY_WATCHDOG_THRESHOLD`
- `ACTIVITY_WATCHDOG_ELAPSED`
- `ACTIVITY_WATCHDOG_CONFIG`
- `ACTIVITY_WATCHDOG_TRIGGERED_AT`

Hook output is surfaced through the app's `Last event` line when the command writes to stdout or stderr. For PowerShell snippets, prefer `Write-Output` over `Write-Host` so the app can capture the text.

Set `alarm` to `true` on a threshold to play the default alarm sequence when that threshold is reached.

Set `banner` to `true` on a threshold to show a Windows banner with `Reset timer` and `Dismiss` buttons. `Reset timer` resets the timer and dismisses the banner; `Dismiss` only hides the banner until the threshold is reached again.