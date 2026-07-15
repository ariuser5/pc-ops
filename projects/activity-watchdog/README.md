# Activity Watchdog

Small console app that shows how long it has been since you last reset a machine-specific activity timer.

## Run

```powershell
dotnet run --project ./projects/activity-watchdog/src/ActivityWatchdog.csproj
```

## Controls

- `R`: reset and restart the timer.
- `S`: stop the timer. There is no resume; resetting starts it again.
- `Q`: quit the app.
- `H`: show the shortcut list in the console area.
- `C`: clear the console details area.

## Auto mode (Windows PoC)

Windows exposes the time since the last keyboard or mouse input, so auto mode does not require an additional package. Enable it in `appsettings.json`:

```json
{
  "mode": "auto",
  "idleCooldown": "00:01:00"
}
```

When the app launches in auto mode, the timer remains at zero until the current Windows session has received no keyboard or mouse input for `idleCooldown`. It then starts automatically and continues running regardless of later input. Resetting it (with `R` or the banner button) returns it to the same waiting state, beginning the next cycle.

This PoC uses the small native Windows `GetLastInputInfo` API. Manual mode remains cross-platform; selecting auto mode on another operating system reports that it is unsupported.

## Config

The app reads `appsettings.json` from the executable folder by default. You can point it somewhere else with `--config`.

```powershell
dotnet run --project ./projects/activity-watchdog/src/ActivityWatchdog.csproj -- --config C:\path\to\appsettings.json
```

Threshold commands are optional and run once per threshold until the timer is reset. The spawned process receives these environment variables:

- `ACTIVITY_WATCHDOG_THRESHOLD`
- `ACTIVITY_WATCHDOG_ELAPSED`
- `ACTIVITY_WATCHDOG_CONFIG`
- `ACTIVITY_WATCHDOG_TRIGGERED_AT`

Hook output is surfaced through the app's `Last event` line when the command writes to stdout or stderr. For PowerShell snippets, prefer `Write-Output` over `Write-Host` so the app can capture the text.

Set `alarm` to `true` on a threshold to play the default alarm sequence when that threshold is reached.

Set `banner` to `true` on a threshold to show a desktop banner with `Reset timer` and `Dismiss` buttons. `Reset timer` resets the timer and dismisses the banner; `Dismiss` only hides the banner until the threshold is reached again. The banner now targets Windows, macOS, and Linux desktop environments.

Time tracking now lives in the separate `projects/timetracker` project.
