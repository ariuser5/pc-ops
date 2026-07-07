using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

var reportService = new DailyTaskReportService(AppContext.BaseDirectory);
var app = new TimeTrackerCli(reportService);
var rootCommand = CommandFactory.Create(app);

try
{
	var parseResult = rootCommand.Parse(args);
	if (parseResult.GetValue(CommandFactory.RefreshStateOption))
	{
		app.RefreshStateSnapshot();

		if (parseResult.CommandResult.Command == rootCommand)
		{
			return 0;
		}
	}

	return await parseResult.InvokeAsync(new InvocationConfiguration());
}
catch (Exception exception)
{
	var originalColor = Console.ForegroundColor;
	Console.ForegroundColor = ConsoleColor.Red;
	Console.Error.WriteLine(exception.Message);
	Console.ForegroundColor = originalColor;
	return 1;
}

internal sealed class TimeTrackerCli
{
	private readonly DailyTaskReportService _reportService;

	public TimeTrackerCli(DailyTaskReportService reportService)
	{
		_reportService = reportService;
	}

	public int ShowStatus()
	{
		var now = DateTimeOffset.Now;
		var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
		WriteLines(FormatStatus(
			_reportService.GetCurrentState(),
			_reportService.GetCurrentTaskSince(now),
			_reportService.GetBreakIntervals(today, now),
			_reportService.BuildRangeSummary(today, today, now)));
		return 0;
	}

	public int EditInterval(DateTimeOffset from, DateTimeOffset to, string taskName)
	{
		WriteLines([_reportService.SetTaskInterval(taskName, from, to).Message]);
		return 0;
	}

	public int SetBreak(DateTimeOffset from, DateTimeOffset to)
	{
		WriteLines([_reportService.SetBreak(from, to).Message]);
		return 0;
	}

	public int SetBreakFromApi(DateTimeOffset from, DateTimeOffset to, bool daily, string? breakRuleName)
	{
		if (daily)
		{
			WriteLines([_reportService.SetRecurringBreak(TimeOnly.FromDateTime(from.LocalDateTime), TimeOnly.FromDateTime(to.LocalDateTime), breakRuleName).Message]);
			return 0;
		}

		if (!string.IsNullOrWhiteSpace(breakRuleName))
		{
			throw new ArgumentException("The --name option can only be used together with --daily.");
		}

		return SetBreak(from, to);
	}

	public int ListBreaks(bool dailyOnly)
	{
		var now = DateTimeOffset.Now;
		var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
		WriteLines(FormatBreakList(_reportService.GetBreakList(today, now), dailyOnly));
		return 0;
	}

	public int RemoveBreak(DateTimeOffset from, DateTimeOffset to)
	{
		WriteLines([_reportService.RemoveBreak(from, to).Message]);
		return 0;
	}

	public int RemoveBreakFromApi(DateTimeOffset? from, DateTimeOffset? to, string? breakRuleId)
	{
		if (!string.IsNullOrWhiteSpace(breakRuleId))
		{
			if (from is not null || to is not null)
			{
				throw new ArgumentException("Break removal accepts either a recurring break id or a --from/--to interval, but not both.");
			}

			WriteLines([_reportService.RemoveRecurringBreak(breakRuleId).Message]);
			return 0;
		}

		if (from is null || to is null)
		{
			throw new ArgumentException("Break removal requires either a recurring break id or both --from and --to.");
		}

		WriteLines([_reportService.RemoveBreak(from.Value, to.Value).Message]);
		return 0;
	}

	public int SetTaskFromApi(string? taskName, DateTimeOffset? from, DateTimeOffset? to)
	{
		if (to is not null && from is null)
		{
			throw new ArgumentException("The --to option requires --from.");
		}

		var effectiveTaskName = taskName;
		if (string.IsNullOrWhiteSpace(effectiveTaskName))
		{
			if (from is null)
			{
				throw new ArgumentException("Task name is required unless you use --from to adjust the current task.");
			}

			effectiveTaskName = _reportService.GetCurrentState().CurrentTask;
		}

		if (to is not null)
		{
			WriteLines([_reportService.SetTaskInterval(effectiveTaskName, from!.Value, to.Value).Message]);
			return 0;
		}

		WriteLines([_reportService.SetTask(effectiveTaskName, from).Message]);
		return 0;
	}

	public int SetTask(string? taskName, DateTimeOffset? since)
	{
		return SetTaskFromApi(taskName, since, null);
	}

	public int SetWorkingHours(TimeOnly start, TimeOnly end)
	{
		WriteLines([_reportService.SetWorkingHours(start, end).Message]);
		return 0;
	}

	public int StopRecording()
	{
		WriteLines([_reportService.StopRecording().Message]);
		return 0;
	}

	public int ResumeRecording()
	{
		WriteLines([_reportService.ResumeRecording().Message]);
		return 0;
	}

	public int RefreshStateSnapshot()
	{
		WriteLines([_reportService.RefreshStateSnapshot().Message]);
		return 0;
	}

	public int ShowReport(DateOnly? fromDate, DateOnly? toDate)
	{
		var reportDate = DateOnly.FromDateTime(DateTime.Now);
		var effectiveFromDate = fromDate ?? toDate ?? reportDate;
		var effectiveToDate = toDate ?? fromDate ?? effectiveFromDate;

		if (effectiveToDate < effectiveFromDate)
		{
			throw new ArgumentException("Report end date must be on or after the start date.");
		}

		WriteLines(FormatTimetrack(
			_reportService.BuildRangeSummary(effectiveFromDate, effectiveToDate, DateTimeOffset.Now),
			_reportService.GetCurrentState()));
		return 0;
	}

	private IReadOnlyList<string> FormatStatus(TrackerState state, DateTimeOffset? currentTaskSince, IReadOnlyList<BreakIntervalEntry> breakIntervals, TimetrackSnapshot todaySnapshot)
	{
		var totalBreakDuration = breakIntervals.Aggregate(TimeSpan.Zero, static (sum, item) => sum + item.Duration);
		var configuredWorkDuration = state.WorkdayEnd - state.WorkdayStart;
		var netWorkDuration = configuredWorkDuration - totalBreakDuration;
		if (netWorkDuration < TimeSpan.Zero)
		{
			netWorkDuration = TimeSpan.Zero;
		}

		var lines = new List<string>
		{
			"TimeTracker status",
			$"Current task: {state.CurrentTask}",
			$"Current task since: {FormatCurrentTaskSince(currentTaskSince)}",
			$"Recording: {(state.IsRecording ? "active" : "paused")}",
			$"Working hours: {state.WorkdayStart:HH\\:mm} - {state.WorkdayEnd:HH\\:mm} | total {FormatElapsed(netWorkDuration)}",
			$"Breaks: {FormatBreakSummary(breakIntervals)}",
			$"Working days: Monday-Friday",
			$"Event log: {_reportService.EventLogPath}",
			$"State file: {_reportService.StateFilePath}",
			string.Empty,
			$"Today's tracked time ({DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd})"
		};

		lines.AddRange(FormatStatusSummary(todaySnapshot));
		return lines;
	}

	private static string FormatBreakSummary(IReadOnlyList<BreakIntervalEntry> breakIntervals)
	{
		if (breakIntervals.Count == 0)
		{
			return "none recorded today";
		}

		var items = breakIntervals
			.Select(static item => $"{item.Start.LocalDateTime:HH:mm}-{item.End.LocalDateTime:HH:mm} ({FormatElapsed(item.Duration)})")
			.ToList();
		var totalBreak = breakIntervals.Aggregate(TimeSpan.Zero, static (sum, item) => sum + item.Duration);
		return $"{string.Join(", ", items)} | total {FormatElapsed(totalBreak)}";
	}

	private static IReadOnlyList<string> FormatBreakList(BreakListSnapshot snapshot, bool dailyOnly)
	{
		var lines = new List<string> { "Breaks" };

		if (!dailyOnly)
		{
			lines.Add($"Effective for {snapshot.Date:yyyy-MM-dd}");
			if (snapshot.EffectiveBreaks.Count == 0)
			{
				lines.Add("No breaks recorded for the selected day.");
			}
			else
			{
				for (var index = 0; index < snapshot.EffectiveBreaks.Count; index++)
				{
					var item = snapshot.EffectiveBreaks[index];
					lines.Add($"{index + 1}. {item.Start.LocalDateTime:HH:mm}-{item.End.LocalDateTime:HH:mm} ({FormatElapsed(item.Duration)})");
				}
			}

			lines.Add(string.Empty);
		}

		lines.Add("Recurring daily breaks");
		if (snapshot.RecurringBreakRules.Count == 0)
		{
			lines.Add("No recurring daily breaks configured.");
		}
		else
		{
			foreach (var rule in snapshot.RecurringBreakRules)
			{
				var duration = rule.To - rule.From;
				lines.Add($"{rule.Id}  {rule.From:HH\\:mm}-{rule.To:HH\\:mm} ({FormatElapsed(duration)})");
			}
		}

		return lines;
	}

	private static IReadOnlyList<string> FormatStatusSummary(TimetrackSnapshot snapshot)
	{
		if (snapshot.Entries.Count == 0)
		{
			return ["No tracked time recorded yet for today."];
		}

		const int taskColumnWidth = 28;
		var lines = new List<string>
		{
			$"{PadOrTrim("Task", taskColumnWidth)}  Duration",
			$"{new string('-', taskColumnWidth)}  --------"
		};

		foreach (var entry in snapshot.Entries)
		{
			lines.Add($"{PadOrTrim(entry.TaskName, taskColumnWidth)}  {FormatElapsed(entry.Duration)}");
		}

		lines.Add($"{new string('-', taskColumnWidth)}  --------");
		lines.Add($"{PadOrTrim("Total", taskColumnWidth)}  {FormatElapsed(snapshot.Total)}");
		return lines;
	}

	private static IReadOnlyList<string> FormatTimetrack(TimetrackSnapshot snapshot, TrackerState currentState)
	{
		var lines = new List<string>
		{
			snapshot.Title,
			$"Current task: {currentState.CurrentTask}",
			$"Recording: {(currentState.IsRecording ? "active" : "paused")}",
			snapshot.SourceDescription,
			string.Empty
		};

		if (snapshot.Entries.Count == 0)
		{
			lines.Add("No tracked time recorded for the selected period.");
			return lines;
		}

		const int taskColumnWidth = 28;
		lines.Add($"{PadOrTrim("Task", taskColumnWidth)}  Hours");
		lines.Add($"{new string('-', taskColumnWidth)}  -----");

		foreach (var entry in snapshot.Entries)
		{
			lines.Add($"{PadOrTrim(entry.TaskName, taskColumnWidth)}  {FormatElapsed(entry.Duration)}");
		}

		lines.Add($"{new string('-', taskColumnWidth)}  -----");
		lines.Add($"{PadOrTrim("Total", taskColumnWidth)}  {FormatElapsed(snapshot.Total)}");
		return lines;
	}

	private static string FormatElapsed(TimeSpan elapsed)
	{
		var rounded = TimeSpan.FromMinutes(Math.Round(elapsed.TotalMinutes, MidpointRounding.AwayFromZero));
		var parts = new List<string>();

		if (rounded.Days > 0)
		{
			parts.Add($"{rounded.Days}d");
		}

		if (rounded.Hours > 0)
		{
			parts.Add($"{rounded.Hours}h");
		}

		if (rounded.Minutes > 0 || parts.Count == 0)
		{
			parts.Add($"{rounded.Minutes}m");
		}

		return string.Concat(parts);
	}

	private static string FormatCurrentTaskSince(DateTimeOffset? currentTaskSince)
	{
		return currentTaskSince is null
			? "n/a"
			: currentTaskSince.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
	}

	private static string PadOrTrim(string value, int width)
	{
		if (value.Length > width)
		{
			return value[..Math.Max(0, width - 1)] + "~";
		}

		return value.PadRight(width);
	}

	private static void WriteLines(IEnumerable<string> lines)
	{
		foreach (var line in lines)
		{
			Console.WriteLine(line);
		}
	}
}

internal static class CommandFactory
{
	public static Option<bool> RefreshStateOption { get; } = CreateRefreshStateOption();

	public static RootCommand Create(TimeTrackerCli app)
	{
		var rootCommand = new RootCommand("Command-style task tracker with persisted workday state.");
		rootCommand.Add(RefreshStateOption);

		rootCommand.Add(CreateStatusCommand(app));
		rootCommand.Add(CreateTaskCommand(app));
		rootCommand.Add(CreateBreakCommand(app));
		rootCommand.Add(CreateHoursCommand(app));
		rootCommand.Add(CreateStopCommand(app));
		rootCommand.Add(CreateResumeCommand(app));
		rootCommand.Add(CreateReportCommand(app));

		rootCommand.Add(CreateRecordingCommand(app));
		rootCommand.Add(CreateLegacySetTaskCommand(app));
		rootCommand.Add(CreateLegacyEditIntervalCommand(app));
		rootCommand.Add(CreateLegacySetBreakCommand(app));
		rootCommand.Add(CreateLegacySetHoursCommand(app));

		return rootCommand;
	}

	private static Option<bool> CreateRefreshStateOption()
	{
		var option = new Option<bool>("--refresh-state", ["--force-sync"]);
		option.Description = "Rebuild tracker-state.json from the event log before running the command.";
		return option;
	}

	private static Command CreateBreakAddCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateTimeOffset>("--from");
		fromOption.Description = "Break start as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseRequiredMomentOption;
		fromOption.Required = true;

		var toOption = new Option<DateTimeOffset>("--to");
		toOption.Description = "Break end as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseRequiredMomentOption;
		toOption.Required = true;

		var command = new Command("add", "Subtract a break interval from tracked work time.");
		command.Hidden = true;
		command.Add(fromOption);
		command.Add(toOption);
		command.SetAction(parseResult => app.SetBreak(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption)));
		return command;
	}

	private static Command CreateBreakCommand(TimeTrackerCli app)
	{
		var command = new Command("break", "Manage break intervals.");
		command.Add(CreateBreakListCommand(app));
		command.Add(CreateBreakRemoveCommand(app));
		command.Add(CreateBreakSetCommand(app));
		command.Add(CreateBreakAddCommand(app));
		return command;
	}

	private static Command CreateBreakListCommand(TimeTrackerCli app)
	{
		var dailyOption = new Option<bool>("--daily");
		dailyOption.Description = "Show only recurring daily break rules.";

		var command = new Command("list", "List today's effective breaks and recurring daily break rules.");
		command.Add(dailyOption);
		command.SetAction(parseResult => app.ListBreaks(parseResult.GetValue(dailyOption)));
		return command;
	}

	private static Command CreateBreakSetCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateTimeOffset>("--from");
		fromOption.Description = "Break start as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseRequiredMomentOption;
		fromOption.Required = true;

		var toOption = new Option<DateTimeOffset>("--to");
		toOption.Description = "Break end as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseRequiredMomentOption;
		toOption.Required = true;

		var dailyOption = new Option<bool>("--daily");
		dailyOption.Description = "Treat the break as a recurring daily break rule.";

		var nameOption = new Option<string?>("--name");
		nameOption.Description = "Custom id to assign to a recurring daily break rule.";

		var command = new Command("set", "Set a break interval by subtracting it from tracked work time.");
		command.Add(fromOption);
		command.Add(toOption);
		command.Add(dailyOption);
		command.Add(nameOption);
		command.SetAction(parseResult => app.SetBreakFromApi(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption),
			parseResult.GetValue(dailyOption),
			parseResult.GetValue(nameOption)));
		return command;
	}

	private static Command CreateBreakRemoveCommand(TimeTrackerCli app)
	{
		var idArgument = new Argument<string?>("break-id")
		{
			Description = "Recurring daily break rule id from 'break list'."
		};
		idArgument.Arity = ArgumentArity.ZeroOrOne;

		var fromOption = new Option<DateTimeOffset?>("--from");
		fromOption.Description = "Break start as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseOptionalMomentOption;

		var toOption = new Option<DateTimeOffset?>("--to");
		toOption.Description = "Break end as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseOptionalMomentOption;

		var command = new Command("remove", "Remove a one-off break interval by --from/--to, or remove a recurring daily break rule by id.");
		command.Add(idArgument);
		command.Add(fromOption);
		command.Add(toOption);
		command.SetAction(parseResult => app.RemoveBreakFromApi(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption),
			parseResult.GetValue(idArgument)));
		return command;
	}

	private static Command CreateHoursCommand(TimeTrackerCli app)
	{
		var command = new Command("hours", "Manage configured working hours.");
		command.Add(CreateHoursSetCommand(app));
		return command;
	}

	private static Command CreateHoursSetCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<TimeOnly>("--from");
		fromOption.Description = "Workday start in HH:mm format.";
		fromOption.CustomParser = ParseRequiredTimeOption;
		fromOption.Required = true;

		var toOption = new Option<TimeOnly>("--to");
		toOption.Description = "Workday end in HH:mm format.";
		toOption.CustomParser = ParseRequiredTimeOption;
		toOption.Required = true;

		var command = new Command("set", "Set working hours in HH:mm format.");
		command.Add(fromOption);
		command.Add(toOption);
		command.SetAction(parseResult => app.SetWorkingHours(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption)));
		return command;
	}

	private static Command CreateLegacyEditIntervalCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateTimeOffset>("--from");
		fromOption.Description = "Interval start as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseRequiredMomentOption;
		fromOption.Required = true;

		var toOption = new Option<DateTimeOffset>("--to");
		toOption.Description = "Interval end as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseRequiredMomentOption;
		toOption.Required = true;

		var taskOption = new Option<string>("--task");
		taskOption.Description = "Task name to assign to the interval.";
		taskOption.Required = true;

		var command = new Command("edit-interval", "Assign a bounded past interval to a task without changing the current task.");
		command.Hidden = true;
		command.Add(fromOption);
		command.Add(toOption);
		command.Add(taskOption);
		command.SetAction(parseResult => app.EditInterval(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption),
			parseResult.GetValue(taskOption) ?? string.Empty));
		return command;
	}

	private static Command CreateReportCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateOnly?>("--from");
		fromOption.Description = "Start date in yyyy-MM-dd format.";
		fromOption.CustomParser = ParseDateOption;

		var toOption = new Option<DateOnly?>("--to");
		toOption.Description = "End date in yyyy-MM-dd format.";
		toOption.CustomParser = ParseDateOption;

		var command = new Command("report", "Show totals for a date interval; defaults to today.");
		command.Add(fromOption);
		command.Add(toOption);
		command.SetAction(parseResult => app.ShowReport(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption)));
		return command;
	}

	private static Command CreateLegacyResumeCommand(TimeTrackerCli app)
	{
		var command = new Command("resume", "Resume automatic workday allocation.");
		command.Hidden = true;
		command.SetAction(_ => app.ResumeRecording());
		return command;
	}

	private static Command CreateLegacySetBreakCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateTimeOffset>("--from");
		fromOption.Description = "Break start as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseRequiredMomentOption;
		fromOption.Required = true;

		var toOption = new Option<DateTimeOffset>("--to");
		toOption.Description = "Break end as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseRequiredMomentOption;
		toOption.Required = true;

		var command = new Command("set-break", "Subtract a break interval from tracked work time.");
		command.Hidden = true;
		command.Add(fromOption);
		command.Add(toOption);
		command.SetAction(parseResult => app.SetBreak(
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption)));
		return command;
	}

	private static Command CreateLegacySetHoursCommand(TimeTrackerCli app)
	{
		var startArgument = new Argument<TimeOnly>("start");
		startArgument.Description = "Workday start in HH:mm format.";
		startArgument.CustomParser = ParseTimeArgument;

		var endArgument = new Argument<TimeOnly>("end");
		endArgument.Description = "Workday end in HH:mm format.";
		endArgument.CustomParser = ParseTimeArgument;

		var command = new Command("set-hours", "Set working hours in HH:mm format.");
		command.Hidden = true;
		command.Add(startArgument);
		command.Add(endArgument);
		command.SetAction(parseResult => app.SetWorkingHours(
			parseResult.GetValue(startArgument),
			parseResult.GetValue(endArgument)));
		return command;
	}

	private static Command CreateLegacySetTaskCommand(TimeTrackerCli app)
	{
		var sinceOption = new Option<DateTimeOffset?>("--since");
		sinceOption.Description = "Backdate the start of the task using HH:mm for today or yyyy-MM-ddTHH:mm.";
		sinceOption.CustomParser = ParseOptionalMomentOption;

		var taskArgument = new Argument<string?>("task-name")
		{
			Description = "Task name to make current."
		};
		taskArgument.Arity = ArgumentArity.ZeroOrOne;

		var command = new Command("set-task", "Set the current task.");
		command.Hidden = true;
		command.Add(sinceOption);
		command.Add(taskArgument);
		command.SetAction(parseResult => app.SetTask(
			parseResult.GetValue(taskArgument),
			parseResult.GetValue(sinceOption)));
		return command;
	}

	private static Command CreateRecordingCommand(TimeTrackerCli app)
	{
		var command = new Command("recording", "Control whether time allocation is active.");
		command.Hidden = true;
		command.Add(CreateRecordingStopCommand(app));
		command.Add(CreateRecordingResumeCommand(app));
		return command;
	}

	private static Command CreateRecordingResumeCommand(TimeTrackerCli app)
	{
		var command = new Command("resume", "Resume automatic workday allocation.");
		command.SetAction(_ => app.ResumeRecording());
		return command;
	}

	private static Command CreateRecordingStopCommand(TimeTrackerCli app)
	{
		var command = new Command("stop", "Pause automatic workday allocation.");
		command.SetAction(_ => app.StopRecording());
		return command;
	}

	private static Command CreateTaskCommand(TimeTrackerCli app)
	{
		var command = new Command("task", "Manage task assignment.");
		command.Add(CreateTaskSetCommand(app));
		return command;
	}

	private static Command CreateTaskSetCommand(TimeTrackerCli app)
	{
		var fromOption = new Option<DateTimeOffset?>("--from");
		fromOption.Description = "Start time as HH:mm for today or yyyy-MM-ddTHH:mm.";
		fromOption.CustomParser = ParseOptionalMomentOption;

		var toOption = new Option<DateTimeOffset?>("--to");
		toOption.Description = "Optional end time as HH:mm for today or yyyy-MM-ddTHH:mm.";
		toOption.CustomParser = ParseOptionalMomentOption;

		var taskArgument = new Argument<string?>("task-name")
		{
			Description = "Task name to assign."
		};
		taskArgument.Arity = ArgumentArity.ZeroOrOne;

		var command = new Command("set", "Set the current task, or assign a bounded task interval when --to is provided.");
		command.Add(fromOption);
		command.Add(toOption);
		command.Add(taskArgument);
		command.SetAction(parseResult => app.SetTaskFromApi(
			parseResult.GetValue(taskArgument),
			parseResult.GetValue(fromOption),
			parseResult.GetValue(toOption)));
		return command;
	}

	private static Command CreateStatusCommand(TimeTrackerCli app)
	{
		var command = new Command("status", "Show current task, hours, and recording state.");
		command.SetAction(_ => app.ShowStatus());
		return command;
	}

	private static Command CreateStopCommand(TimeTrackerCli app)
	{
		var command = new Command("stop", "Pause automatic workday allocation.");
		command.SetAction(_ => app.StopRecording());
		return command;
	}

	private static Command CreateResumeCommand(TimeTrackerCli app)
	{
		var command = new Command("resume", "Resume automatic workday allocation.");
		command.SetAction(_ => app.ResumeRecording());
		return command;
	}

	private static DateOnly? ParseDateOption(ArgumentResult argumentResult)
	{
		if (argumentResult.Tokens.Count == 0)
		{
			return null;
		}

		var rawValue = argumentResult.Tokens.Single().Value;
		if (TryParseReportDateAlias(rawValue, out var aliasedDate))
		{
			return aliasedDate;
		}

		if (DateOnly.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			return date;
		}

		argumentResult.AddError($"Invalid date '{rawValue}'. Use yyyy-MM-dd, '.' , or 'today'.");
		return null;
	}

	private static bool TryParseReportDateAlias(string rawValue, out DateOnly date)
	{
		if (rawValue == "." || string.Equals(rawValue, "today", StringComparison.OrdinalIgnoreCase))
		{
			date = DateOnly.FromDateTime(DateTime.Now);
			return true;
		}

		date = default;
		return false;
	}

	private static DateTimeOffset? ParseOptionalMomentOption(ArgumentResult argumentResult)
	{
		if (argumentResult.Tokens.Count == 0)
		{
			return null;
		}

		return TryParseMoment(argumentResult.Tokens.Single().Value, out var timestamp)
			? timestamp
			: AddMomentParseError(argumentResult);
	}

	private static DateTimeOffset ParseRequiredMomentOption(ArgumentResult argumentResult)
	{
		if (argumentResult.Tokens.Count == 0)
		{
			argumentResult.AddError("Missing timestamp. Use HH:mm for today or yyyy-MM-ddTHH:mm.");
			return default;
		}

		return TryParseMoment(argumentResult.Tokens.Single().Value, out var timestamp)
			? timestamp
			: AddMomentParseError(argumentResult) ?? default;
	}

	private static DateTimeOffset? AddMomentParseError(ArgumentResult argumentResult)
	{
		var rawValue = argumentResult.Tokens.Single().Value;
		argumentResult.AddError($"Invalid timestamp '{rawValue}'. Use HH:mm for today or yyyy-MM-ddTHH:mm.");
		return null;
	}

	private static TimeOnly ParseRequiredTimeOption(ArgumentResult argumentResult)
	{
		if (argumentResult.Tokens.Count == 0)
		{
			argumentResult.AddError("Missing time. Use HH:mm.");
			return default;
		}

		var rawValue = argumentResult.Tokens.Single().Value;
		if (TryParseTime(rawValue, out var time))
		{
			return time;
		}

		argumentResult.AddError($"Invalid time '{NormalizeTimeSeparators(rawValue)}'. Use HH:mm.");
		return default;
	}

	private static TimeOnly ParseTimeArgument(ArgumentResult argumentResult)
	{
		var rawValue = argumentResult.Tokens.Single().Value;
		if (TryParseTime(rawValue, out var time))
		{
			return time;
		}

		argumentResult.AddError($"Invalid time '{NormalizeTimeSeparators(rawValue)}'. Use HH:mm.");
		return default;
	}

	private static bool TryParseMoment(string rawValue, out DateTimeOffset timestamp)
	{
		rawValue = NormalizeTimeSeparators(rawValue);

		if (TimeOnly.TryParseExact(rawValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
		{
			var today = DateOnly.FromDateTime(DateTime.Now);
			timestamp = new DateTimeOffset(today.ToDateTime(time, DateTimeKind.Local));
			return true;
		}

		var formats = new[]
		{
			"yyyy-MM-ddTHH:mm",
			"yyyy-MM-dd HH:mm",
			"yyyy-MM-ddTHH:mm:ss",
			"yyyy-MM-dd HH:mm:ss"
		};

		if (DateTime.TryParseExact(rawValue, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDateTime))
		{
			timestamp = new DateTimeOffset(DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Local));
			return true;
		}

		timestamp = default;
		return false;
	}

	private static string NormalizeTimeSeparators(string rawValue)
	{
		return rawValue.Replace(';', ':');
	}

	private static bool TryParseTime(string rawValue, out TimeOnly time)
	{
		return TimeOnly.TryParseExact(NormalizeTimeSeparators(rawValue), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
	}
}
