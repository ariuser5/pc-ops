using System.Text.Json;

internal sealed class DailyTaskReportService
{
	public const string DefaultTaskName = "Generic-Task";

	private static readonly TimeOnly DefaultWorkdayEnd = new(17, 0);
	private static readonly TimeOnly DefaultWorkdayStart = new(9, 0);

	private const string InitializedEvent = "INITIALIZED";
	private const string RecordingResumedEvent = "RECORDING_RESUMED";
	private const string RecordingStoppedEvent = "RECORDING_STOPPED";
	private const string TaskSetEvent = "TASK_SET";
	private const string WorkingHoursSetEvent = "WORKING_HOURS_SET";

	public DailyTaskReportService(string appBaseDirectory)
	{
		var appRoot = ResolveAppRoot(appBaseDirectory);
		ReportDirectoryPath = Path.Combine(appRoot, "reports");
		Directory.CreateDirectory(ReportDirectoryPath);
		EventLogPath = Path.Combine(ReportDirectoryPath, "tracker-events.jsonl");
		StateFilePath = Path.Combine(ReportDirectoryPath, "tracker-state.json");
	}

	public string EventLogPath { get; }

	public string ReportDirectoryPath { get; }

	public string StateFilePath { get; }

	public TrackerState GetCurrentState()
	{
		return LoadState() ?? CreateInitialState(DateTimeOffset.Now);
	}

	public TrackerMutationResult ResumeRecording()
	{
		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);

		if (state.IsRecording)
		{
			return new TrackerMutationResult(false, "Recording is already active.", state);
		}

		var updatedState = state with { IsRecording = true };
		SaveState(updatedState);
		AppendEvent(RecordingResumedEvent, updatedState, now);
		return new TrackerMutationResult(true, "Recording resumed.", updatedState);
	}

	public TrackerMutationResult SetTask(string taskName)
	{
		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		var normalizedTask = NormalizeTaskName(taskName);

		if (string.Equals(state.CurrentTask, normalizedTask, StringComparison.Ordinal))
		{
			return new TrackerMutationResult(false, $"Current task remains '{state.CurrentTask}'.", state);
		}

		var updatedState = state with { CurrentTask = normalizedTask };
		SaveState(updatedState);
		AppendEvent(TaskSetEvent, updatedState, now);
		return new TrackerMutationResult(true, $"Current task: {updatedState.CurrentTask}", updatedState);
	}

	public TrackerMutationResult SetWorkingHours(TimeOnly start, TimeOnly end)
	{
		if (end <= start)
		{
			throw new ArgumentException("Working hour end must be later than start.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);

		if (state.WorkdayStart == start && state.WorkdayEnd == end)
		{
			return new TrackerMutationResult(false, $"Working hours remain {start:HH\\:mm} - {end:HH\\:mm}.", state);
		}

		var updatedState = state with { WorkdayStart = start, WorkdayEnd = end };
		SaveState(updatedState);
		AppendEvent(WorkingHoursSetEvent, updatedState, now);
		return new TrackerMutationResult(true, $"Working hours: {start:HH\\:mm} - {end:HH\\:mm}", updatedState);
	}

	public TrackerMutationResult StopRecording()
	{
		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);

		if (!state.IsRecording)
		{
			return new TrackerMutationResult(false, "Recording is already paused.", state);
		}

		var updatedState = state with { IsRecording = false };
		SaveState(updatedState);
		AppendEvent(RecordingStoppedEvent, updatedState, now);
		return new TrackerMutationResult(true, "Recording paused.", updatedState);
	}

	public TimetrackSnapshot BuildRangeSummary(DateOnly start, DateOnly end, DateTimeOffset now)
	{
		if (end < start)
		{
			throw new ArgumentException("Report end date must be on or after the start date.");
		}

		var rangeStart = AtLocal(start, TimeOnly.MinValue);
		var rangeEndExclusive = AtLocal(end.AddDays(1), TimeOnly.MinValue);
		var cappedRangeEnd = Min(rangeEndExclusive, now);
		var totals = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

		if (cappedRangeEnd > rangeStart)
		{
			var events = ReadEvents()
				.Where(static trackerEvent => trackerEvent.State.TrackingStartedAt is not null)
				.OrderBy(static trackerEvent => trackerEvent.Timestamp)
				.ToList();

			if (events.Count == 0)
			{
				var fallbackState = GetCurrentState();
				if (fallbackState.TrackingStartedAt is not null)
				{
					events.Add(new TrackerEvent(fallbackState.TrackingStartedAt.Value, InitializedEvent, fallbackState));
				}
			}

			for (var index = 0; index < events.Count; index++)
			{
				var segmentStart = Max(events[index].Timestamp, rangeStart);
				var segmentEnd = index + 1 < events.Count
					? Min(events[index + 1].Timestamp, cappedRangeEnd)
					: cappedRangeEnd;

				AddSegmentDurations(totals, events[index].State, segmentStart, segmentEnd);
			}
		}

		var entries = totals
			.OrderByDescending(static entry => entry.Value)
			.ThenBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
			.Select(static entry => new TaskDurationEntry(entry.Key, entry.Value))
			.ToList();

		var totalDuration = entries.Aggregate(TimeSpan.Zero, static (sum, entry) => sum + entry.Duration);
		var sourceDescription = $"Event log: {EventLogPath} | Working days: Monday-Friday";
		return new TimetrackSnapshot(
			$"Time Tracking for {start:yyyy-MM-dd} .. {end:yyyy-MM-dd}",
			sourceDescription,
			entries,
			totalDuration);
	}

	public static string NormalizeTaskName(string? taskName)
	{
		return string.IsNullOrWhiteSpace(taskName) ? DefaultTaskName : taskName.Trim();
	}

	private static void AddDuration(IDictionary<string, TimeSpan> totals, string taskName, TimeSpan duration)
	{
		if (duration <= TimeSpan.Zero)
		{
			return;
		}

		var normalizedTask = NormalizeTaskName(taskName);
		if (!totals.TryGetValue(normalizedTask, out var existing))
		{
			existing = TimeSpan.Zero;
		}

		totals[normalizedTask] = existing + duration;
	}

	private static void AddSegmentDurations(IDictionary<string, TimeSpan> totals, TrackerState state, DateTimeOffset segmentStart, DateTimeOffset segmentEnd)
	{
		if (!state.IsRecording || segmentEnd <= segmentStart)
		{
			return;
		}

		var cursor = segmentStart;
		while (cursor < segmentEnd)
		{
			var currentDate = DateOnly.FromDateTime(cursor.LocalDateTime.Date);
			var dayEnd = Min(segmentEnd, AtLocal(currentDate.AddDays(1), TimeOnly.MinValue));

			if (IsWorkingDay(currentDate.DayOfWeek))
			{
				var workdayStart = AtLocal(currentDate, state.WorkdayStart);
				var workdayEnd = AtLocal(currentDate, state.WorkdayEnd);
				var effectiveStart = Max(cursor, workdayStart);
				var effectiveEnd = Min(dayEnd, workdayEnd);

				if (effectiveEnd > effectiveStart)
				{
					AddDuration(totals, state.CurrentTask, effectiveEnd - effectiveStart);
				}
			}

			cursor = dayEnd;
		}
	}

	private void AppendEvent(string name, TrackerState state, DateTimeOffset timestamp)
	{
		var serializedEvent = JsonSerializer.Serialize(new TrackerEvent(timestamp, name, state));
		File.AppendAllText(EventLogPath, serializedEvent + Environment.NewLine);
	}

	private TrackerState EnsureInitialized(DateTimeOffset now)
	{
		var state = LoadState();
		if (state is not null)
		{
			return state;
		}

		var initializedState = CreateInitialState(now);
		SaveState(initializedState);
		AppendEvent(InitializedEvent, initializedState, initializedState.TrackingStartedAt!.Value);
		return initializedState;
	}

	private TrackerState CreateInitialState(DateTimeOffset now)
	{
		var initializationTimestamp = Min(now, AtLocal(DateOnly.FromDateTime(now.LocalDateTime.Date), DefaultWorkdayStart));
		return TrackerState.Default with { TrackingStartedAt = initializationTimestamp };
	}

	private static bool IsWorkingDay(DayOfWeek dayOfWeek)
	{
		return dayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
	}

	private TrackerState? LoadState()
	{
		if (File.Exists(StateFilePath))
		{
			var stateJson = File.ReadAllText(StateFilePath);
			var state = JsonSerializer.Deserialize<TrackerState>(stateJson);
			if (state is not null)
			{
				return state;
			}
		}

		var lastEvent = ReadEvents().LastOrDefault();
		return lastEvent?.State;
	}

	private IEnumerable<TrackerEvent> ReadEvents()
	{
		if (!File.Exists(EventLogPath))
		{
			yield break;
		}

		foreach (var line in File.ReadLines(EventLogPath))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			TrackerEvent? trackerEvent;
			try
			{
				trackerEvent = JsonSerializer.Deserialize<TrackerEvent>(line);
			}
			catch (JsonException)
			{
				continue;
			}

			if (trackerEvent is not null)
			{
				yield return trackerEvent;
			}
		}
	}

	private static DateTimeOffset AtLocal(DateOnly date, TimeOnly time)
	{
		return new DateTimeOffset(date.ToDateTime(time, DateTimeKind.Local));
	}

	private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
	{
		return left >= right ? left : right;
	}

	private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
	{
		return left <= right ? left : right;
	}

	private void SaveState(TrackerState state)
	{
		File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static string ResolveAppRoot(string appBaseDirectory)
	{
		var current = Path.GetFullPath(appBaseDirectory);

		for (var index = 0; index < 8; index++)
		{
			if (File.Exists(Path.Combine(current, "src", "TimeTracker.csproj")))
			{
				return current;
			}

			var parent = Directory.GetParent(current);

			if (parent is null)
			{
				break;
			}

			current = parent.FullName;
		}

		return Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", ".."));
	}
}

internal sealed record TrackerEvent(DateTimeOffset Timestamp, string Name, TrackerState State);

internal sealed record TrackerMutationResult(bool Changed, string Message, TrackerState State);

internal sealed record TrackerState(string CurrentTask, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, bool IsRecording, DateTimeOffset? TrackingStartedAt)
{
	public static TrackerState Default => new(DailyTaskReportService.DefaultTaskName, new TimeOnly(9, 0), new TimeOnly(17, 0), true, null);
}

internal sealed record TimetrackSnapshot(string Title, string SourceDescription, IReadOnlyList<TaskDurationEntry> Entries, TimeSpan Total);

internal sealed record TaskDurationEntry(string TaskName, TimeSpan Duration);
