using System.Text.Json;

internal sealed class DailyTaskReportService
{
	public const string DefaultTaskName = "Generic-Task";
	public const string BreakTaskName = "__BREAK__";

	private static readonly TimeOnly DefaultWorkdayEnd = new(17, 0);
	private static readonly TimeOnly DefaultWorkdayStart = new(9, 0);

	private const string InitializedEvent = "INITIALIZED";
	private const string BreakDailyRemoveEvent = "BREAK_DAILY_REMOVE";
	private const string BreakDailySetEvent = "BREAK_DAILY_SET";
	private const string BreakRemoveEvent = "BREAK_REMOVE";
	private const string BreakSetEvent = "BREAK_SET";
	private const string IntervalEditedEvent = "INTERVAL_EDITED";
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

	public StateRefreshResult RefreshStateSnapshot()
	{
		var eventState = ReadEvents().LastOrDefault()?.State;
		if (eventState is null)
		{
			var snapshotState = LoadStateSnapshot();
			return snapshotState is null
				? new StateRefreshResult(false, "No event log state found; nothing to refresh.")
				: new StateRefreshResult(false, "No event log state found; existing snapshot left unchanged.");
		}

		var snapshotBeforeRefresh = LoadStateSnapshot();
		var changed = snapshotBeforeRefresh != eventState;
		SaveState(eventState);
		return new StateRefreshResult(changed, changed
			? "State snapshot refreshed from the event log."
			: "State snapshot already matches the event log.");
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

	public TrackerMutationResult EditInterval(DateTimeOffset from, DateTimeOffset to, string taskName)
	{
		return SetTaskInterval(taskName, from, to);
	}

	public TrackerMutationResult SetTaskInterval(string taskName, DateTimeOffset from, DateTimeOffset to)
	{
		if (to <= from)
		{
			throw new ArgumentException("Edited interval end must be later than start.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		var normalizedTask = NormalizeTaskName(taskName);
		var taskContext = GetTaskSegmentContext(normalizedTask, from, to, now);

		if (taskContext is not null && from > taskContext.Start)
		{
			var resetTask = taskContext.PreviousTaskName ?? DefaultTaskName;
			AppendIntervalCorrection(state, now, taskContext.Start, from, resetTask);
		}

		AppendIntervalCorrection(state, now, from, to, normalizedTask);
		return new TrackerMutationResult(true, $"Corrected interval {FormatInterval(from, to)} to {normalizedTask}.", state);
	}

	public TrackerMutationResult SetBreak(DateTimeOffset from, DateTimeOffset to)
	{
		if (to <= from)
		{
			throw new ArgumentException("Break end must be later than start.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		AppendBreak(state, now, from, to);
		return new TrackerMutationResult(true, $"Recorded break {FormatInterval(from, to)}.", state);
	}

	public TrackerMutationResult RemoveBreak(DateTimeOffset from, DateTimeOffset to)
	{
		if (to <= from)
		{
			throw new ArgumentException("Break end must be later than start.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		AppendBreakRemoval(state, now, from, to);
		return new TrackerMutationResult(true, $"Removed break {FormatInterval(from, to)}.", state);
	}

	public TrackerMutationResult SetRecurringBreak(TimeOnly from, TimeOnly to, string? breakRuleId = null)
	{
		if (to <= from)
		{
			throw new ArgumentException("Recurring break end must be later than start.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		var requestedBreakRuleId = string.IsNullOrWhiteSpace(breakRuleId)
			? GenerateRecurringBreakRuleId()
			: breakRuleId.Trim();
		var recurringRules = BuildActiveRecurringBreakRules();
		if (recurringRules.ContainsKey(requestedBreakRuleId))
		{
			throw new ArgumentException($"A recurring break with id '{requestedBreakRuleId}' already exists. Choose a different --name value or remove the existing rule first.");
		}

		var breakRule = new RecurringBreakRule(requestedBreakRuleId, from, to);
		AppendRecurringBreak(state, now, breakRule);
		return new TrackerMutationResult(true, $"Recorded daily break {FormatTimeRange(from, to)} with id {breakRule.Id}.", state);
	}

	public TrackerMutationResult RemoveRecurringBreak(string breakRuleId)
	{
		if (string.IsNullOrWhiteSpace(breakRuleId))
		{
			throw new ArgumentException("Recurring break id is required.");
		}

		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		var normalizedBreakRuleId = breakRuleId.Trim();
		var recurringRules = BuildActiveRecurringBreakRules();
		if (!recurringRules.ContainsKey(normalizedBreakRuleId))
		{
			throw new ArgumentException($"No recurring break found with id '{normalizedBreakRuleId}'. Use 'break list' to inspect active recurring breaks.");
		}

		AppendRecurringBreakRemoval(state, now, normalizedBreakRuleId);
		return new TrackerMutationResult(true, $"Removed daily break {normalizedBreakRuleId}.", state);
	}

	public TrackerMutationResult SetTask(string taskName, DateTimeOffset? since = null)
	{
		var now = DateTimeOffset.Now;
		var state = EnsureInitialized(now);
		var normalizedTask = NormalizeTaskName(taskName);
		var taskChanged = !string.Equals(state.CurrentTask, normalizedTask, StringComparison.Ordinal);
		var updatedState = taskChanged ? state with { CurrentTask = normalizedTask } : state;
		var currentTaskContext = taskChanged ? null : GetCurrentTaskContext(state.CurrentTask, now);

		if (since is not null)
		{
			if (since.Value >= now)
			{
				throw new ArgumentException("The --since time must be earlier than now.");
			}

			if (currentTaskContext is not null && since.Value > currentTaskContext.Start)
			{
				var resetTask = currentTaskContext.PreviousTaskName ?? DefaultTaskName;
				AppendIntervalCorrection(updatedState, now, currentTaskContext.Start, since.Value, resetTask);
			}

			AppendIntervalCorrection(updatedState, now, since.Value, now, normalizedTask);
		}

		if (!taskChanged)
		{
			var unchangedMessage = since is null
				? $"Current task remains '{state.CurrentTask}'."
				: $"Current task remains '{state.CurrentTask}'. Corrected interval {FormatInterval(since.Value, now)}.";
			return new TrackerMutationResult(since is not null, unchangedMessage, state);
		}

		SaveState(updatedState);
		AppendEvent(TaskSetEvent, updatedState, now);
		var changedMessage = since is null
			? $"Current task: {updatedState.CurrentTask}"
			: $"Current task: {updatedState.CurrentTask}. Corrected interval {FormatInterval(since.Value, now)}.";
		return new TrackerMutationResult(true, changedMessage, updatedState);
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

	public DateTimeOffset? GetCurrentTaskSince(DateTimeOffset now)
	{
		var currentState = GetCurrentState();
		var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
		var segments = BuildSegments(AtLocal(today, TimeOnly.MinValue), now, now, applyBreaks: false);
		var activeSegment = segments
			.Where(segment => segment.TaskName.Equals(currentState.CurrentTask, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(static segment => segment.End)
			.ThenByDescending(static segment => segment.Start)
			.FirstOrDefault();

		if (activeSegment is null)
		{
			return null;
		}

		return activeSegment.Start;
	}

	public IReadOnlyList<BreakIntervalEntry> GetBreakIntervals(DateOnly date, DateTimeOffset now)
	{
		var rangeStart = AtLocal(date, TimeOnly.MinValue);
		var rangeEnd = AtLocal(date.AddDays(1), TimeOnly.MinValue);
		if (rangeEnd <= rangeStart)
		{
			return [];
		}

		return BuildEffectiveBreakSegments(rangeStart, rangeEnd)
			.Select(static segment => new BreakIntervalEntry(segment.Start, segment.End))
			.ToList();
	}

	public BreakListSnapshot GetBreakList(DateOnly date, DateTimeOffset now)
	{
		return new BreakListSnapshot(date, GetBreakIntervals(date, now), GetRecurringBreakRules());
	}

	public IReadOnlyList<RecurringBreakRuleEntry> GetRecurringBreakRules()
	{
		return BuildActiveRecurringBreakRules()
			.Values
			.OrderBy(static rule => rule.From)
			.ThenBy(static rule => rule.To)
			.ThenBy(static rule => rule.Id, StringComparer.OrdinalIgnoreCase)
			.Select(static rule => new RecurringBreakRuleEntry(rule.Id, rule.From, rule.To))
			.ToList();
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
		var segments = BuildSegments(rangeStart, cappedRangeEnd, now);

		foreach (var segment in segments)
		{
			AddDuration(totals, segment.TaskName, segment.End - segment.Start);
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

	private static void AddSegment(List<WorkSegment> segments, string taskName, DateTimeOffset segmentStart, DateTimeOffset segmentEnd)
	{
		if (segmentEnd <= segmentStart)
		{
			return;
		}

		var normalizedTask = NormalizeTaskName(taskName);
		if (segments.Count > 0)
		{
			var last = segments[^1];
			if (last.TaskName.Equals(normalizedTask, StringComparison.OrdinalIgnoreCase) && last.End == segmentStart)
			{
				segments[^1] = last with { End = segmentEnd };
				return;
			}
		}

		segments.Add(new WorkSegment(segmentStart, segmentEnd, normalizedTask));
	}

	private static void AddStateSegments(List<WorkSegment> segments, TrackerState state, DateTimeOffset segmentStart, DateTimeOffset segmentEnd)
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
					AddSegment(segments, state.CurrentTask, effectiveStart, effectiveEnd);
				}
			}

			cursor = dayEnd;
		}
	}

	private static List<WorkSegment> ApplyCorrections(List<WorkSegment> baseSegments, IReadOnlyList<TrackerEvent> events, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		var segments = baseSegments;

		var correctionEvents = events
			.Where(static item => item.Correction is not null && !IsBreakEvent(item.Name))
			.OrderBy(static item => item.Timestamp);

		foreach (var trackerEvent in correctionEvents)
		{
			foreach (var correctionSegment in BuildCorrectionSegments(trackerEvent, rangeStart, rangeEnd))
			{
				segments = OverlaySegment(segments, correctionSegment);
			}
		}

		return MergeSegments(segments);
	}

	private void AppendEvent(string name, TrackerState state, DateTimeOffset timestamp, IntervalCorrection? correction = null, RecurringBreakRule? breakRule = null, BreakRuleReference? breakRuleReference = null)
	{
		var serializedEvent = JsonSerializer.Serialize(new TrackerEvent(timestamp, name, state, correction, breakRule, breakRuleReference));
		File.AppendAllText(EventLogPath, serializedEvent + Environment.NewLine);
	}

	private void AppendIntervalCorrection(TrackerState state, DateTimeOffset recordedAt, DateTimeOffset from, DateTimeOffset to, string taskName)
	{
		AppendEvent(
			IntervalEditedEvent,
			state,
			recordedAt,
			new IntervalCorrection(from, to, NormalizeTaskName(taskName)));
	}

	private void AppendBreak(TrackerState state, DateTimeOffset recordedAt, DateTimeOffset from, DateTimeOffset to)
	{
		AppendEvent(
			BreakSetEvent,
			state,
			recordedAt,
			new IntervalCorrection(from, to, BreakTaskName));
	}

	private void AppendBreakRemoval(TrackerState state, DateTimeOffset recordedAt, DateTimeOffset from, DateTimeOffset to)
	{
		AppendEvent(
			BreakRemoveEvent,
			state,
			recordedAt,
			new IntervalCorrection(from, to, BreakTaskName));
	}

	private void AppendRecurringBreak(TrackerState state, DateTimeOffset recordedAt, RecurringBreakRule breakRule)
	{
		AppendEvent(
			BreakDailySetEvent,
			state,
			recordedAt,
			breakRule: breakRule);
	}

	private void AppendRecurringBreakRemoval(TrackerState state, DateTimeOffset recordedAt, string breakRuleId)
	{
		AppendEvent(
			BreakDailyRemoveEvent,
			state,
			recordedAt,
			breakRuleReference: new BreakRuleReference(breakRuleId));
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

	private static IReadOnlyList<WorkSegment> BuildCorrectionSegments(TrackerEvent trackerEvent, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		if (trackerEvent.Correction is null)
		{
			return [];
		}

		var correction = trackerEvent.Correction;
		var correctionStart = Max(correction.From, rangeStart);
		var correctionEnd = Min(correction.To, rangeEnd);
		var segments = new List<WorkSegment>();

		if (correctionEnd <= correctionStart)
		{
			return segments;
		}

		var cursor = correctionStart;
		while (cursor < correctionEnd)
		{
			var currentDate = DateOnly.FromDateTime(cursor.LocalDateTime.Date);
			var dayEnd = Min(correctionEnd, AtLocal(currentDate.AddDays(1), TimeOnly.MinValue));

			if (IsWorkingDay(currentDate.DayOfWeek))
			{
				var workdayStart = AtLocal(currentDate, trackerEvent.State.WorkdayStart);
				var workdayEnd = AtLocal(currentDate, trackerEvent.State.WorkdayEnd);
				var effectiveStart = Max(cursor, workdayStart);
				var effectiveEnd = Min(dayEnd, workdayEnd);

				if (effectiveEnd > effectiveStart)
				{
					AddSegment(segments, correction.TaskName, effectiveStart, effectiveEnd);
				}
			}

			cursor = dayEnd;
		}

		return segments;
	}

	private static string FormatInterval(DateTimeOffset from, DateTimeOffset to)
	{
		return $"{from.LocalDateTime:yyyy-MM-dd HH:mm} .. {to.LocalDateTime:yyyy-MM-dd HH:mm}";
	}

	private static string FormatTimeRange(TimeOnly from, TimeOnly to)
	{
		return $"{from:HH\\:mm} .. {to:HH\\:mm}";
	}

	private static string GenerateRecurringBreakRuleId()
	{
		return Guid.NewGuid().ToString("N")[..8];
	}

	private CurrentTaskContext? GetCurrentTaskContext(string currentTaskName, DateTimeOffset now)
	{
		var currentState = GetCurrentState();
		var rangeStart = currentState.TrackingStartedAt ?? CreateInitialState(now).TrackingStartedAt ?? now;
		var segments = BuildSegments(rangeStart, now, now, applyBreaks: false);
		var currentSegment = segments
			.Where(segment => segment.TaskName.Equals(currentTaskName, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(static segment => segment.End)
			.ThenByDescending(static segment => segment.Start)
			.FirstOrDefault();

		if (currentSegment is null)
		{
			return null;
		}

		var previousSegment = segments
			.Where(segment => segment.End <= currentSegment.Start)
			.OrderByDescending(static segment => segment.End)
			.ThenByDescending(static segment => segment.Start)
			.FirstOrDefault();

		return new CurrentTaskContext(currentSegment.Start, previousSegment?.TaskName);
	}

	private TaskSegmentContext? GetTaskSegmentContext(string taskName, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
	{
		var currentState = GetCurrentState();
		var rangeStart = currentState.TrackingStartedAt ?? CreateInitialState(now).TrackingStartedAt ?? from;
		var rangeEnd = Max(now, to);
		var segments = BuildSegments(rangeStart, rangeEnd, rangeEnd, applyBreaks: false)
			.OrderBy(static segment => segment.Start)
			.ThenBy(static segment => segment.End)
			.ToList();

		var matchIndex = segments.FindIndex(segment =>
			segment.TaskName.Equals(taskName, StringComparison.OrdinalIgnoreCase)
			&& segment.End > from
			&& segment.Start < to);

		if (matchIndex < 0)
		{
			return null;
		}

		var previousTaskName = matchIndex > 0 ? segments[matchIndex - 1].TaskName : null;
		var nextTaskName = matchIndex + 1 < segments.Count ? segments[matchIndex + 1].TaskName : null;
		return new TaskSegmentContext(segments[matchIndex].Start, segments[matchIndex].End, previousTaskName, nextTaskName);
	}

	private static IReadOnlyList<TrackerEvent> GetStateTimelineEvents(IReadOnlyList<TrackerEvent> events, DateTimeOffset now)
	{
		var stateEvents = events
			.Where(static trackerEvent => trackerEvent.State.TrackingStartedAt is not null && IsStateTimelineEvent(trackerEvent.Name))
			.ToList();

		if (stateEvents.Count > 0)
		{
			return stateEvents;
		}

		var fallbackState = TrackerState.Default with { TrackingStartedAt = Min(now, AtLocal(DateOnly.FromDateTime(now.LocalDateTime.Date), DefaultWorkdayStart)) };
		return [new TrackerEvent(fallbackState.TrackingStartedAt!.Value, InitializedEvent, fallbackState)];
	}

	private static bool IsStateTimelineEvent(string eventName)
	{
		return eventName is InitializedEvent or RecordingResumedEvent or RecordingStoppedEvent or TaskSetEvent or WorkingHoursSetEvent;
	}

	private List<WorkSegment> BuildEffectiveBreakSegments(DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		var segments = BuildRecurringBreakSegments(rangeStart, rangeEnd);
		var breakEvents = ReadEvents()
			.Where(static trackerEvent => trackerEvent.Name == BreakSetEvent || trackerEvent.Name == BreakRemoveEvent)
			.OrderBy(static trackerEvent => trackerEvent.Timestamp);

		foreach (var trackerEvent in breakEvents)
		{
			var breakSegments = BuildCorrectionSegments(trackerEvent, rangeStart, rangeEnd);
			foreach (var breakSegment in breakSegments)
			{
				segments = IsBreakSetEvent(trackerEvent.Name)
					? OverlaySegment(segments, breakSegment)
					: RemoveSegment(segments, breakSegment);
			}
		}

		return MergeSegments(segments);
	}

	private Dictionary<string, RecurringBreakRule> BuildActiveRecurringBreakRules()
	{
		var activeRules = new Dictionary<string, RecurringBreakRule>(StringComparer.OrdinalIgnoreCase);
		var recurringEvents = ReadEvents()
			.Where(static trackerEvent => trackerEvent.Name is BreakDailySetEvent or BreakDailyRemoveEvent)
			.OrderBy(static trackerEvent => trackerEvent.Timestamp);

		foreach (var trackerEvent in recurringEvents)
		{
			switch (trackerEvent.Name)
			{
				case BreakDailySetEvent when trackerEvent.BreakRule is not null:
					activeRules[trackerEvent.BreakRule.Id] = trackerEvent.BreakRule;
					break;
				case BreakDailyRemoveEvent when trackerEvent.BreakRuleReference is not null:
					activeRules.Remove(trackerEvent.BreakRuleReference.Id);
					break;
			}
		}

		return activeRules;
	}

	private List<WorkSegment> BuildRecurringBreakSegments(DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		if (rangeEnd <= rangeStart)
		{
			return [];
		}

		var segments = new List<WorkSegment>();
		var activeRules = new Dictionary<string, ActiveRecurringBreakRule>(StringComparer.OrdinalIgnoreCase);
		var recurringEvents = ReadEvents()
			.Where(static trackerEvent => trackerEvent.Name is BreakDailySetEvent or BreakDailyRemoveEvent)
			.OrderBy(static trackerEvent => trackerEvent.Timestamp);

		foreach (var trackerEvent in recurringEvents)
		{
			var eventDate = DateOnly.FromDateTime(trackerEvent.Timestamp.LocalDateTime.Date);
			switch (trackerEvent.Name)
			{
				case BreakDailySetEvent when trackerEvent.BreakRule is not null:
					activeRules[trackerEvent.BreakRule.Id] = new ActiveRecurringBreakRule(trackerEvent.BreakRule, eventDate);
					break;
				case BreakDailyRemoveEvent when trackerEvent.BreakRuleReference is not null:
					if (activeRules.Remove(trackerEvent.BreakRuleReference.Id, out var activeRule))
					{
						AddRecurringBreakSegments(segments, activeRule, eventDate, rangeStart, rangeEnd);
					}
					break;
			}
		}

		var rangeEndExclusiveDate = DateOnly.FromDateTime(rangeEnd.LocalDateTime.Date).AddDays(rangeEnd.TimeOfDay == TimeSpan.Zero ? 0 : 1);
		foreach (var activeRule in activeRules.Values)
		{
			AddRecurringBreakSegments(segments, activeRule, rangeEndExclusiveDate, rangeStart, rangeEnd);
		}

		return MergeSegments(segments);
	}

	private static void AddRecurringBreakSegments(List<WorkSegment> segments, ActiveRecurringBreakRule activeRule, DateOnly effectiveEndDateExclusive, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		var startDate = Max(activeRule.ActiveFromDate, DateOnly.FromDateTime(rangeStart.LocalDateTime.Date));
		var endDateExclusive = Min(effectiveEndDateExclusive, DateOnly.FromDateTime(rangeEnd.LocalDateTime.Date).AddDays(rangeEnd.TimeOfDay == TimeSpan.Zero ? 0 : 1));

		for (var date = startDate; date < endDateExclusive; date = date.AddDays(1))
		{
			if (!IsWorkingDay(date.DayOfWeek))
			{
				continue;
			}

			var breakStart = AtLocal(date, activeRule.Rule.From);
			var breakEnd = AtLocal(date, activeRule.Rule.To);
			var effectiveStart = Max(rangeStart, breakStart);
			var effectiveEnd = Min(rangeEnd, breakEnd);

			if (effectiveEnd > effectiveStart)
			{
				AddSegment(segments, BreakTaskName, effectiveStart, effectiveEnd);
			}
		}
	}

	private List<WorkSegment> BuildSegments(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, DateTimeOffset now, bool applyBreaks = true)
	{
		if (rangeEnd <= rangeStart)
		{
			return [];
		}

		var events = ReadEvents()
			.OrderBy(static trackerEvent => trackerEvent.Timestamp)
			.ToList();
		var stateEvents = GetStateTimelineEvents(events, now);
		var segments = BuildBaseSegments(stateEvents, rangeStart, rangeEnd);
		segments = ApplyCorrections(segments, events, rangeStart, rangeEnd);

		if (!applyBreaks)
		{
			return segments;
		}

		foreach (var breakSegment in BuildEffectiveBreakSegments(rangeStart, rangeEnd))
		{
			segments = RemoveSegment(segments, breakSegment);
		}

		return segments;
	}

	private static List<WorkSegment> BuildBaseSegments(IReadOnlyList<TrackerEvent> stateEvents, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
	{
		var segments = new List<WorkSegment>();

		for (var index = 0; index < stateEvents.Count; index++)
		{
			var segmentStart = Max(stateEvents[index].Timestamp, rangeStart);
			var segmentEnd = index + 1 < stateEvents.Count
				? Min(stateEvents[index + 1].Timestamp, rangeEnd)
				: rangeEnd;

			AddStateSegments(segments, stateEvents[index].State, segmentStart, segmentEnd);
		}

		return segments;
	}

	private static bool IsWorkingDay(DayOfWeek dayOfWeek)
	{
		return dayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
	}

	private static List<WorkSegment> MergeSegments(IEnumerable<WorkSegment> segments)
	{
		var orderedSegments = segments.OrderBy(static item => item.Start).ThenBy(static item => item.End).ToList();
		if (orderedSegments.Count == 0)
		{
			return orderedSegments;
		}

		var merged = new List<WorkSegment> { orderedSegments[0] };
		for (var index = 1; index < orderedSegments.Count; index++)
		{
			var current = orderedSegments[index];
			var last = merged[^1];

			if (last.TaskName.Equals(current.TaskName, StringComparison.OrdinalIgnoreCase) && last.End >= current.Start)
			{
				merged[^1] = last with { End = Max(last.End, current.End) };
				continue;
			}

			merged.Add(current);
		}

		return merged;
	}

	private static List<WorkSegment> OverlaySegment(List<WorkSegment> segments, WorkSegment overlay)
	{
		var updatedSegments = new List<WorkSegment>();
		var inserted = false;

		foreach (var segment in segments.OrderBy(static item => item.Start).ThenBy(static item => item.End))
		{
			if (segment.End <= overlay.Start)
			{
				updatedSegments.Add(segment);
				continue;
			}

			if (segment.Start >= overlay.End)
			{
				if (!inserted)
				{
					updatedSegments.Add(overlay);
					inserted = true;
				}

				updatedSegments.Add(segment);
				continue;
			}

			if (segment.Start < overlay.Start)
			{
				updatedSegments.Add(segment with { End = overlay.Start });
			}

			if (!inserted)
			{
				updatedSegments.Add(overlay);
				inserted = true;
			}

			if (segment.End > overlay.End)
			{
				updatedSegments.Add(segment with { Start = overlay.End });
			}
		}

		if (!inserted)
		{
			updatedSegments.Add(overlay);
		}

		return MergeSegments(updatedSegments);
	}

	private static List<WorkSegment> RemoveSegment(List<WorkSegment> segments, WorkSegment removal)
	{
		var updatedSegments = new List<WorkSegment>();

		foreach (var segment in segments.OrderBy(static item => item.Start).ThenBy(static item => item.End))
		{
			if (segment.End <= removal.Start || segment.Start >= removal.End)
			{
				updatedSegments.Add(segment);
				continue;
			}

			if (segment.Start < removal.Start)
			{
				updatedSegments.Add(segment with { End = removal.Start });
			}

			if (segment.End > removal.End)
			{
				updatedSegments.Add(segment with { Start = removal.End });
			}
		}

		return MergeSegments(updatedSegments);
	}

	private static bool IsBreakEvent(string eventName)
	{
		return eventName is BreakSetEvent or BreakRemoveEvent or BreakDailySetEvent or BreakDailyRemoveEvent;
	}

	private static bool IsBreakSetEvent(string eventName)
	{
		return eventName is BreakSetEvent or BreakDailySetEvent;
	}

	private TrackerState? LoadState()
	{
		var eventState = ReadEvents().LastOrDefault()?.State;
		if (eventState is not null)
		{
			var snapshotState = LoadStateSnapshot();
			if (snapshotState != eventState)
			{
				SaveState(eventState);
			}

			return eventState;
		}

		return LoadStateSnapshot();
	}

	private TrackerState? LoadStateSnapshot()
	{
		if (!File.Exists(StateFilePath))
		{
			return null;
		}

		try
		{
			var stateJson = File.ReadAllText(StateFilePath);
			return JsonSerializer.Deserialize<TrackerState>(stateJson);
		}
		catch (JsonException)
		{
			return null;
		}
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

	private static DateOnly Max(DateOnly left, DateOnly right)
	{
		return left >= right ? left : right;
	}

	private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
	{
		return left <= right ? left : right;
	}

	private static DateOnly Min(DateOnly left, DateOnly right)
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

internal sealed record TrackerEvent(DateTimeOffset Timestamp, string Name, TrackerState State, IntervalCorrection? Correction = null, RecurringBreakRule? BreakRule = null, BreakRuleReference? BreakRuleReference = null);

internal sealed record IntervalCorrection(DateTimeOffset From, DateTimeOffset To, string TaskName);

internal sealed record RecurringBreakRule(string Id, TimeOnly From, TimeOnly To);

internal sealed record BreakRuleReference(string Id);

internal sealed record TrackerMutationResult(bool Changed, string Message, TrackerState State);

internal sealed record TrackerState(string CurrentTask, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, bool IsRecording, DateTimeOffset? TrackingStartedAt)
{
	public static TrackerState Default => new(DailyTaskReportService.DefaultTaskName, new TimeOnly(9, 0), new TimeOnly(17, 0), true, null);
}

internal sealed record TimetrackSnapshot(string Title, string SourceDescription, IReadOnlyList<TaskDurationEntry> Entries, TimeSpan Total);

internal sealed record TaskDurationEntry(string TaskName, TimeSpan Duration);

internal sealed record WorkSegment(DateTimeOffset Start, DateTimeOffset End, string TaskName);

internal sealed record BreakIntervalEntry(DateTimeOffset Start, DateTimeOffset End)
{
	public TimeSpan Duration => End - Start;
}

internal sealed record BreakListSnapshot(DateOnly Date, IReadOnlyList<BreakIntervalEntry> EffectiveBreaks, IReadOnlyList<RecurringBreakRuleEntry> RecurringBreakRules);

internal sealed record RecurringBreakRuleEntry(string Id, TimeOnly From, TimeOnly To);

internal sealed record CurrentTaskContext(DateTimeOffset Start, string? PreviousTaskName);

internal sealed record TaskSegmentContext(DateTimeOffset Start, DateTimeOffset End, string? PreviousTaskName, string? NextTaskName);

internal sealed record ActiveRecurringBreakRule(RecurringBreakRule Rule, DateOnly ActiveFromDate);

internal sealed record StateRefreshResult(bool Changed, string Message);
