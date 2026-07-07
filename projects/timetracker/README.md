# TimeTracker

Command-style console app for task tracking and time summaries.

The app does not need to stay open. You run a command when you want to change state or inspect a report.

## Run

```powershell
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- <command>
```

Examples:

```powershell
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- task set Feature-Work
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- task set Feature-Work --from 10:15
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- task set --from 10:15
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- task set Ticket-123 --from 09:00 --to 11:00
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- --refresh-state status
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- break set --from 12:00 --to 12:30
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- break remove --from 12:00 --to 12:30
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- break set --from 13:00 --to 14:00 --daily --name Lunch
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- break list --daily
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- break remove Lunch
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- hours set --from 09:00 --to 17:00
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- stop
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- resume
dotnet run --project ./projects/timetracker/src/TimeTracker.csproj -- report --from 2026-07-01 --to 2026-07-31
```

## Commands

- `status`: show the current task, its effective start time, today's tracked totals, and today's recorded breaks.
- `task set [task-name] [--from HH:mm|HH;mm|yyyy-MM-ddTHH:mm] [--to HH:mm|HH;mm|yyyy-MM-ddTHH:mm]`: the canonical task command.
	Without `--to`, it sets the current task and optionally backdates its start.
	With `--from` and `--to`, it assigns a bounded task interval.
- `break set --from HH:mm|HH;mm|yyyy-MM-ddTHH:mm --to HH:mm|HH;mm|yyyy-MM-ddTHH:mm [--daily] [--name <id>]`: set a one-off break, or a recurring daily break rule when `--daily` is present. Use `--name` to provide a custom unique id for the recurring rule.
- `break remove --from HH:mm|HH;mm|yyyy-MM-ddTHH:mm --to HH:mm|HH;mm|yyyy-MM-ddTHH:mm`: remove a one-off break interval, or cancel today's instance of a recurring break.
- `break remove <id>`: remove a recurring daily break rule by id.
- `break list [--daily]`: list today's effective breaks and, optionally, recurring daily break rules. With `--daily`, only recurring rules are shown.
- `hours set --from HH:mm|HH;mm --to HH:mm|HH;mm`: set working hours.
- `stop`: pause automatic workday allocation.
- `resume`: resume automatic workday allocation.
- `report [--from yyyy-MM-dd] [--to yyyy-MM-dd]`: show a time summary for a date interval. If no dates are provided, it reports today.
- `--refresh-state`: rebuild `tracker-state.json` from the event log before running a command. You can also run it by itself.
- `help`: show help.

Legacy compatibility aliases still work, but they are no longer the documented API:

- `set-task` maps to `task set`
- `edit-interval` maps to bounded `task set --from ... --to ...`
- `set-break` maps to `break set`
- `break add` still works as an alias for `break set`
- `set-hours` maps to `hours set`
- `recording stop` and `recording resume` still work as aliases for `stop` and `resume`

## Tracking Model

- Default task: `Generic-Task`
- Default work window: `09:00` to `17:00`
- Default working days: Monday through Friday
- If you do not change task during a workday, the whole work window is assigned to the current task.
- `task set --from` corrects the interval from the given time until now. If you also pass a task name, it becomes the current task; if you omit the task name, it corrects the current task's start time.
- `task set --from ... --to ...` assigns a bounded slice of time to a task. If the same task was already active earlier, moving `--from` later naturally restores the earlier slice to the previous task.
- `break set` removes a bounded slice of time from all tracked work, so lunch or pause time does not count toward any task.
- To modify a one-off break, remove the old interval and set the new one. Example: `break remove --from 13:00 --to 14:00` followed by `break set --from 13:15 --to 13:45`.
- `break set --daily` creates a recurring daily break rule, such as a lunch break. `--name` lets you choose a stable custom id like `Lunch`; otherwise an id is auto-generated. `break remove` can cancel just today's instance, `break list --daily` shows rule ids, and `break remove <id>` deletes the recurring rule itself.
- `stop` and `resume` pause and restart automatic allocation without changing the current task.

## Data Files

The app stores append-only tracking data under `projects/timetracker/reports/`.

- `tracker-events.jsonl`: the append-only event log. Commands write structured events here, such as task changes, work-hour updates, stop/resume events, bounded corrections, one-off breaks, break removals, and recurring daily break rules.
- `tracker-state.json`: a persisted snapshot of the latest state for quick reads. Reports still rebuild the timeline from `tracker-events.jsonl`; this file is a convenience cache, not the source of history.

If you ever suspect the snapshot drifted from the log, run `timetracker --refresh-state` or `timetracker --refresh-state status`.
