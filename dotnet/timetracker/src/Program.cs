using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

var reportService = new DailyTaskReportService(AppContext.BaseDirectory);
var app = new TimeTrackerCli(reportService);
var rootCommand = CommandFactory.Create(app);

try
{
	var parseResult = rootCommand.Parse(args);
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
		WriteLines(FormatStatus(
			_reportService.GetCurrentState(),
			_reportService.BuildRangeSummary(DateOnly.FromDateTime(now.LocalDateTime.Date), DateOnly.FromDateTime(now.LocalDateTime.Date), now)));
		return 0;
	}

	public int SetTask(string taskName)
	{
		WriteLines([_reportService.SetTask(taskName).Message]);
		return 0;
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

	private IReadOnlyList<string> FormatStatus(TrackerState state, TimetrackSnapshot todaySnapshot)
	{
		var lines = new List<string>
		{
			"TimeTracker status",
			$"Current task: {state.CurrentTask}",
			$"Recording: {(state.IsRecording ? "active" : "paused")}",
			$"Working hours: {state.WorkdayStart:HH\\:mm} - {state.WorkdayEnd:HH\\:mm}",
			$"Working days: Monday-Friday",
			$"Event log: {_reportService.EventLogPath}",
			$"State file: {_reportService.StateFilePath}",
			string.Empty,
			$"Today's tracked time ({DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd})"
		};

		lines.AddRange(FormatStatusSummary(todaySnapshot));
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

		if (rounded.TotalHours >= 24)
		{
			return $"{(int)rounded.TotalDays}d {rounded.Hours:00}:{rounded.Minutes:00}";
		}

		return $"{(int)rounded.TotalHours:00}:{rounded.Minutes:00}";
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
	public static RootCommand Create(TimeTrackerCli app)
	{
		var rootCommand = new RootCommand("Command-style task tracker with persisted workday state.");

		rootCommand.Add(CreateStatusCommand(app));
		rootCommand.Add(CreateSetTaskCommand(app));
		rootCommand.Add(CreateSetHoursCommand(app));
		rootCommand.Add(CreateStopCommand(app));
		rootCommand.Add(CreateResumeCommand(app));
		rootCommand.Add(CreateReportCommand(app));

		return rootCommand;
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

	private static Command CreateResumeCommand(TimeTrackerCli app)
	{
		var command = new Command("resume", "Resume automatic workday allocation.");
		command.SetAction(_ => app.ResumeRecording());
		return command;
	}

	private static Command CreateSetHoursCommand(TimeTrackerCli app)
	{
		var startArgument = new Argument<TimeOnly>("start");
		startArgument.Description = "Workday start in HH:mm format.";
		startArgument.CustomParser = ParseTimeArgument;

		var endArgument = new Argument<TimeOnly>("end");
		endArgument.Description = "Workday end in HH:mm format.";
		endArgument.CustomParser = ParseTimeArgument;

		var command = new Command("set-hours", "Set working hours in HH:mm format.");
		command.Add(startArgument);
		command.Add(endArgument);
		command.SetAction(parseResult => app.SetWorkingHours(
			parseResult.GetValue(startArgument),
			parseResult.GetValue(endArgument)));
		return command;
	}

	private static Command CreateSetTaskCommand(TimeTrackerCli app)
	{
		var taskArgument = new Argument<string>("task-name")
		{
			Description = "Task name to make current."
		};

		var command = new Command("set-task", "Set the current task.");
		command.Add(taskArgument);
		command.SetAction(parseResult => app.SetTask(parseResult.GetValue(taskArgument) ?? string.Empty));
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

	private static DateOnly? ParseDateOption(ArgumentResult argumentResult)
	{
		if (argumentResult.Tokens.Count == 0)
		{
			return null;
		}

		var rawValue = argumentResult.Tokens.Single().Value;
		if (DateOnly.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			return date;
		}

		argumentResult.AddError($"Invalid date '{rawValue}'. Use yyyy-MM-dd.");
		return null;
	}

	private static TimeOnly ParseTimeArgument(ArgumentResult argumentResult)
	{
		var rawValue = argumentResult.Tokens.Single().Value;
		if (TimeOnly.TryParseExact(rawValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
		{
			return time;
		}

		argumentResult.AddError($"Invalid time '{rawValue}'. Use HH:mm.");
		return default;
	}
}
