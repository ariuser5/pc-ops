# TimeTracker

Command-style console app for task tracking and time summaries.

The app does not need to stay open. You run a command when you want to change state or inspect a report.

## Run

```powershell
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- <command>
```

Examples:

```powershell
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- set-task Feature-Work
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- set-hours 09:00 17:00
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- stop
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- resume
dotnet run --project ./dotnet/timetracker/src/TimeTracker.csproj -- report --from 2026-07-01 --to 2026-07-31
```

## Commands

- `status`: show the current task, work hours, and whether recording is active.
- `set-task <task-name>`: set the current task.
- `set-hours <start> <end>`: set working hours in `HH:mm` format.
- `stop`: pause automatic workday allocation.
- `resume`: resume automatic workday allocation.
- `report [--from yyyy-MM-dd] [--to yyyy-MM-dd]`: show a time summary for a date interval. If no dates are provided, it reports today.
- `help`: show help.

## Tracking Model

- Default task: `Generic-Task`
- Default work window: `09:00` to `17:00`
- Default working days: Monday through Friday
- If you do not change task during a workday, the whole work window is assigned to the current task.
- `stop` and `resume` let you pause and restart automatic allocation without changing the current task.

## Data Files

The app stores append-only tracking data under `dotnet/timetracker/reports/`.

- `tracker-events.jsonl`: timestamped state changes such as task switches, work-hour updates, stop, and resume.
- `tracker-state.json`: the latest persisted state for quick reads.
