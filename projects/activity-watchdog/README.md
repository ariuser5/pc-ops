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

## Automatic reset (Windows PoC)

Windows exposes the last keyboard or mouse input, so automatic reset does not require an additional package. Enable it by specifying `autoResetCooldown` in `appsettings.json`:

```json
{
  "autoResetCooldown": "00:05:00"
}
```

The timer always starts immediately. If fresh input is detected within `autoResetCooldown` after a start or reset, the timer restarts from zero. The automatic reset also clears triggered thresholds and dismisses an active banner, just like resetting with `R` or the banner button.

Omit `autoResetCooldown` to disable input monitoring and use the timer manually. When configured, its value must be greater than zero.

This PoC uses the small native Windows `GetLastInputInfo` API. Without `autoResetCooldown`, the app remains cross-platform; configuring it on another operating system reports that it is unsupported.

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
