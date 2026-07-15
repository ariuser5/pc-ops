using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

try
{
	var arguments = CommandLineArguments.Parse(args);

	if (arguments.ShowHelp)
	{
		HelpPrinter.Write();
		return 0;
	}

	var configPath = Path.GetFullPath(arguments.ConfigPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
	var config = AppConfigLoader.Load(configPath);

	using var app = new ActivityWatchdogApp(config, configPath);
	return app.Run();
}
catch (Exception exception)
{
	var originalColor = Console.ForegroundColor;
	Console.ForegroundColor = ConsoleColor.Red;
	Console.Error.WriteLine(exception.Message);
	Console.ForegroundColor = originalColor;
	return 1;
}

internal sealed class ActivityWatchdogApp : IDisposable
{
	private const string ResetBannerButtonText = "Reset timer";

	private readonly AppConfig _config;
	private readonly string _configPath;
	private readonly DesktopBannerService? _bannerService;
	private readonly object _stateLock = new();
	private readonly ThresholdState[] _thresholds;
	private readonly Stopwatch _idleCooldownStopwatch = new();
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private readonly CancellationTokenSource _shutdown = new();

	private bool _isStopped;
	private bool _isWaitingForIdle;
	private bool _shouldExit;
	private int _lastRenderLineCount;
	private TimeSpan _stoppedElapsed = TimeSpan.Zero;
	private DateTimeOffset _lastResetAt = DateTimeOffset.Now;
	private string _lastEventMessage = "Timer started.";

	public ActivityWatchdogApp(AppConfig config, string configPath)
	{
		_config = config;
		_configPath = configPath;

		if (config.Mode == TimerMode.Auto && !OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Auto mode currently supports Windows only.");
		}

		_bannerService = DesktopBannerService.TryCreate(() => Reset("banner"));
		_thresholds = config.Thresholds.Select(threshold => new ThresholdState(threshold)).ToArray();

		if (config.Mode == TimerMode.Auto)
		{
			_stopwatch.Reset();
			_idleCooldownStopwatch.Start();
			_isWaitingForIdle = true;
			_lastEventMessage = $"Waiting for {_config.IdleCooldown} of no input before starting timer.";
		}
	}

	public int Run()
	{
		Console.Clear();
		Console.CursorVisible = false;
		Task? uiLoopTask = null;

		try
		{
			uiLoopTask = Task.Run(RunUiLoop);
			RunInputLoop();
			return 0;
		}
		finally
		{
			_shutdown.Cancel();

			if (uiLoopTask is not null)
			{
				try
				{
					uiLoopTask.Wait();
				}
				catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
				{
				}
			}

			Console.ResetColor();
			Console.CursorVisible = true;
			Console.WriteLine();
		}
	}

	public void Dispose()
	{
		_shutdown.Cancel();
		_bannerService?.Dispose();
	}

	private void RunInputLoop()
	{
		if (Console.IsInputRedirected)
		{
			_shutdown.Token.WaitHandle.WaitOne();
			return;
		}

		while (!_shutdown.IsCancellationRequested && !_shouldExit)
		{
			var keyInfo = Console.ReadKey(intercept: true);

			if (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
			{
				RequestQuit();
				continue;
			}

			switch (keyInfo.Key)
			{
				case ConsoleKey.R:
					Reset("key");
					break;
				case ConsoleKey.S:
					StopTimer();
					break;
				case ConsoleKey.Q:
					RequestQuit();
					break;
				case ConsoleKey.H:
					ShowHelpHint();
					break;
				case ConsoleKey.C:
					ClearDetailsArea();
					break;
			}
		}
	}

	private void RunUiLoop()
	{
		while (!_shutdown.IsCancellationRequested)
		{
			lock (_stateLock)
			{
				TryStartAfterIdleCooldown();
				var elapsed = GetElapsed();
				var activeThreshold = _isWaitingForIdle ? null : EvaluateThresholds(elapsed);
				Render(elapsed, activeThreshold);
			}

			try
			{
				Task.Delay(_config.RefreshIntervalMs, _shutdown.Token).Wait(_shutdown.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void Reset(string source)
	{
		lock (_stateLock)
		{
			if (_config.Mode == TimerMode.Auto)
			{
				_stopwatch.Reset();
				_idleCooldownStopwatch.Restart();
				_isWaitingForIdle = true;
			}
			else
			{
				_stopwatch.Restart();
				_idleCooldownStopwatch.Reset();
				_isWaitingForIdle = false;
			}

			_isStopped = false;
			_stoppedElapsed = TimeSpan.Zero;
			_lastResetAt = DateTimeOffset.Now;
			_lastEventMessage = _isWaitingForIdle
				? $"Timer reset{(source == "banner" ? " from banner" : string.Empty)}; waiting for {_config.IdleCooldown} of no input."
				: source == "banner" ? "Timer reset from banner." : "Timer reset.";

			foreach (var threshold in _thresholds)
			{
				threshold.HasTriggered = false;
			}
		}

		_bannerService?.DismissActiveBanner();
	}

	private bool StopTimer()
	{
		lock (_stateLock)
		{
			if (_isStopped)
			{
				_lastEventMessage = "Timer is already stopped.";
				return false;
			}

			_stoppedElapsed = _stopwatch.Elapsed;
			_isWaitingForIdle = false;
			_isStopped = true;
			_lastEventMessage = $"Timer stopped at {FormatElapsed(_stoppedElapsed)}.";
			return true;
		}
	}

	private void RequestQuit()
	{
		lock (_stateLock)
		{
			if (_shouldExit)
			{
				return;
			}

			_lastEventMessage = "Quitting.";
			_shouldExit = true;
		}

		_bannerService?.DismissActiveBanner();
		_shutdown.Cancel();
	}

	private TimeSpan GetElapsed()
	{
		return _isStopped ? _stoppedElapsed : _stopwatch.Elapsed;
	}

	private void TryStartAfterIdleCooldown()
	{
		if (!_isWaitingForIdle
			|| _idleCooldownStopwatch.Elapsed < _config.IdleCooldown
			|| WindowsUserIdleTime.Get() < _config.IdleCooldown)
		{
			return;
		}

		_isWaitingForIdle = false;
		_idleCooldownStopwatch.Reset();
		_stopwatch.Restart();
		_lastResetAt = DateTimeOffset.Now;
		_lastEventMessage = $"Timer started automatically after {_config.IdleCooldown} of no input.";
	}

	private ThresholdState? EvaluateThresholds(TimeSpan elapsed)
	{
		ThresholdState? activeThreshold = null;

		foreach (var threshold in _thresholds)
		{
			if (elapsed < threshold.Config.Duration)
			{
				break;
			}

			activeThreshold = threshold;

			if (threshold.HasTriggered)
			{
				continue;
			}

			threshold.HasTriggered = true;
			var dispatch = ThresholdHookRunner.QueueActions(threshold.Config, elapsed, _configPath, message =>
			{
				lock (_stateLock)
				{
					_lastEventMessage = message;
				}
			}) with
			{
				BannerQueued = TryShowBanner(threshold.Config, elapsed)
			};

			_lastEventMessage = $"Threshold '{threshold.Config.Name}' reached at {FormatElapsed(elapsed)}.{FormatDispatchSummary(dispatch)}";
		}

		return activeThreshold;
	}

	private bool TryShowBanner(ThresholdConfig config, TimeSpan elapsed)
	{
		if (!config.Banner)
		{
			return false;
		}

		if (_bannerService is null)
		{
			_lastEventMessage = $"Banner for '{config.Name}' is not available in this environment.";
			return false;
		}

		return _bannerService.ShowBanner(
			title: config.Name,
			message: $"Threshold reached after {FormatElapsed(elapsed)}.",
			buttonText: ResetBannerButtonText);
	}

	private void Render(TimeSpan elapsed, ThresholdState? activeThreshold)
	{
		var width = GetRenderWidth();
		var statusColor = _isStopped || _isWaitingForIdle
			? ConsoleColor.DarkGray
			: activeThreshold?.DisplayColor ?? ConsoleColor.Green;

		var nextThreshold = _thresholds.FirstOrDefault(threshold => elapsed < threshold.Config.Duration);
		var state = _isStopped ? "stopped" : _isWaitingForIdle ? "waiting for idle" : "running";
		var stateLine = $"State: {state} | Mode: {_config.Mode.ToString().ToLowerInvariant()} | Last start/reset: {_lastResetAt:yyyy-MM-dd HH:mm:ss}";

		var lines = new List<(string Text, ConsoleColor? Color)>
		{
			("Activity Watchdog", ConsoleColor.Cyan),
			($"Elapsed: {FormatElapsed(elapsed)}", statusColor),
			(stateLine, null),
			($"Next threshold: {DescribeNextThreshold(nextThreshold)}", null),
			($"Last event: {_lastEventMessage}", null),
			($"Config: {_configPath}", ConsoleColor.DarkGray),
			("Controls: [R] reset  [S] stop  [Q] quit  [H] help  [C] clear", ConsoleColor.DarkGray)
		};

		try
		{
			Console.SetCursorPosition(0, 0);
		}
		catch
		{
			return;
		}

		foreach (var line in lines)
		{
			Console.ForegroundColor = line.Color ?? ConsoleColor.Gray;
			Console.WriteLine(FitToWidth(line.Text, width));
		}

		for (var lineIndex = lines.Count; lineIndex < _lastRenderLineCount; lineIndex++)
		{
			Console.WriteLine(new string(' ', width));
		}

		_lastRenderLineCount = lines.Count;
		Console.ResetColor();
	}

	private void ShowHelpHint()
	{
		_lastEventMessage = "Controls: R reset, S stop, Q quit, H help, C clear.";
	}

	private void ClearDetailsArea()
	{
		_lastEventMessage = "Details cleared.";
	}

	private static string DescribeNextThreshold(ThresholdState? nextThreshold)
	{
		if (nextThreshold is null)
		{
			return "none remaining";
		}

		return $"{nextThreshold.Config.Name} at {FormatElapsed(nextThreshold.Config.Duration)}";
	}

	private static string FitToWidth(string text, int width)
	{
		if (text.Length >= width)
		{
			return text[..Math.Max(1, width - 1)];
		}

		return text.PadRight(width);
	}

	private static string FormatElapsed(TimeSpan elapsed)
	{
		if (elapsed.TotalDays >= 1)
		{
			return $"{(int)elapsed.TotalDays}d {elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
		}

		return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
	}

	private static string FormatDispatchSummary(ThresholdActionDispatch dispatch)
	{
		var messages = new List<string>(2);

		if (dispatch.CommandQueued)
		{
			messages.Add("Hook queued.");
		}

		if (dispatch.AlarmQueued)
		{
			messages.Add("Alarm queued.");
		}

		if (dispatch.BannerQueued)
		{
			messages.Add("Banner shown.");
		}

		return messages.Count == 0 ? string.Empty : $" {string.Join(" ", messages)}";
	}

	private static int GetRenderWidth()
	{
		try
		{
			return Math.Max(Console.WindowWidth - 1, 40);
		}
		catch
		{
			return 120;
		}
	}
}

internal sealed record CommandLineArguments(bool ShowHelp, string? ConfigPath)
{
	public static CommandLineArguments Parse(string[] args)
	{
		var showHelp = false;
		string? configPath = null;

		for (var index = 0; index < args.Length; index++)
		{
			switch (args[index])
			{
				case "--help":
				case "-h":
					showHelp = true;
					break;
				case "--config":
					if (index + 1 >= args.Length)
					{
						throw new ArgumentException("Missing value for --config.");
					}

					configPath = args[++index];
					break;
				default:
					throw new ArgumentException($"Unknown argument '{args[index]}'. Use --help to see available options.");
			}
		}

		return new CommandLineArguments(showHelp, configPath);
	}
}

internal static class HelpPrinter
{
	public static void Write()
	{
		Console.WriteLine("Activity Watchdog");
		Console.WriteLine();
		Console.WriteLine("Usage:");
		Console.WriteLine("  ActivityWatchdog [--config <path>] [--help]");
		Console.WriteLine();
		Console.WriteLine("Controls while running:");
		Console.WriteLine("  R  Reset and restart the timer");
		Console.WriteLine("  S  Stop the timer; reset starts it again");
		Console.WriteLine("  Q  Quit the app");
		Console.WriteLine("  H  Show the shortcut list");
		Console.WriteLine("  C  Clear the details area");
		Console.WriteLine();
		Console.WriteLine("Notes:");
		Console.WriteLine("  Threshold commands run once per threshold until the timer is reset.");
		Console.WriteLine("  Hook output is surfaced through the Last event line when the command writes to stdout or stderr.");
		Console.WriteLine("  Set alarm to true on a threshold to play the default alarm sequence.");
	}
}

internal static class AppConfigLoader
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		AllowTrailingCommas = true,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter<TimerMode>(JsonNamingPolicy.CamelCase) }
	};

	public static AppConfig Load(string configPath)
	{
		if (!File.Exists(configPath))
		{
			throw new FileNotFoundException($"Config file not found: {configPath}");
		}

		using var stream = File.OpenRead(configPath);
		var config = JsonSerializer.Deserialize<AppConfig>(stream, SerializerOptions) ?? new AppConfig();
		config.Normalize();
		return config;
	}
}

internal sealed class AppConfig
{
	public int RefreshIntervalMs { get; set; } = 250;

	public TimerMode Mode { get; set; } = TimerMode.Manual;

	public TimeSpan IdleCooldown { get; set; } = TimeSpan.FromMinutes(1);

	public List<ThresholdConfig> Thresholds { get; set; } = [];

	public void Normalize()
	{
		RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 100, 10_000);

		if (IdleCooldown <= TimeSpan.Zero)
		{
			throw new InvalidDataException("idleCooldown must be greater than zero.");
		}

		Thresholds = Thresholds
			.Where(threshold => threshold.Duration > TimeSpan.Zero)
			.OrderBy(threshold => threshold.Duration)
			.Select((threshold, index) =>
			{
				threshold.Name = string.IsNullOrWhiteSpace(threshold.Name)
					? $"Threshold {index + 1}"
					: threshold.Name.Trim();

				return threshold;
			})
			.ToList();
	}
}

internal enum TimerMode
{
	Manual,
	Auto
}

internal static class WindowsUserIdleTime
{
	public static TimeSpan Get()
	{
		var input = new LastInputInfo
		{
			Size = (uint)Marshal.SizeOf<LastInputInfo>()
		};

		if (!GetLastInputInfo(ref input))
		{
			throw new InvalidOperationException("Windows did not provide the last user input time.");
		}

		var idleMilliseconds = unchecked((uint)Environment.TickCount - input.TickCount);
		return TimeSpan.FromMilliseconds(idleMilliseconds);
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetLastInputInfo(ref LastInputInfo input);

	[StructLayout(LayoutKind.Sequential)]
	private struct LastInputInfo
	{
		public uint Size;
		public uint TickCount;
	}
}

internal sealed class ThresholdConfig
{
	public string Name { get; set; } = string.Empty;

	public TimeSpan Duration { get; set; } = TimeSpan.Zero;

	public string? Color { get; set; }

	public string? Command { get; set; }

	public bool Alarm { get; set; }

	public bool Banner { get; set; }
}

internal sealed class ThresholdState
{
	public ThresholdState(ThresholdConfig config)
	{
		Config = config;
		DisplayColor = ConsoleColorParser.Parse(config.Color);
	}

	public ThresholdConfig Config { get; }

	public ConsoleColor? DisplayColor { get; }

	public bool HasTriggered { get; set; }
}

internal static class ConsoleColorParser
{
	public static ConsoleColor? Parse(string? rawColor)
	{
		if (string.IsNullOrWhiteSpace(rawColor))
		{
			return null;
		}

		return Enum.TryParse<ConsoleColor>(rawColor, ignoreCase: true, out var color)
			? color
			: null;
	}
}

internal static class ThresholdHookRunner
{
	private const int AlarmFrequency = 1200;
	private const int AlarmDurationMs = 250;
	private const int AlarmRepeatCount = 3;
	private const int AlarmDelayMs = 125;

	public static ThresholdActionDispatch QueueActions(ThresholdConfig config, TimeSpan elapsed, string configPath, Action<string> notify)
	{
		var commandQueued = false;
		var alarmQueued = false;

		if (!string.IsNullOrWhiteSpace(config.Command))
		{
			_ = Task.Run(() => RunCommandAsync(config, elapsed, configPath, notify));
			commandQueued = true;
		}

		if (config.Alarm)
		{
			_ = Task.Run(() => PlayAlarm(config, notify));
			alarmQueued = true;
		}

		return new ThresholdActionDispatch(commandQueued, alarmQueued);
	}

	private static async Task RunCommandAsync(ThresholdConfig config, TimeSpan elapsed, string configPath, Action<string> notify)
	{
		try
		{
			using var process = new Process
			{
				StartInfo = CreateStartInfo(config.Command!, config, elapsed, configPath)
			};

			process.Start();

			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();

			await process.WaitForExitAsync();

			var stdout = await stdoutTask;
			var stderr = await stderrTask;
			var output = FirstNonEmptyLine(stdout) ?? FirstNonEmptyLine(stderr);

			if (process.ExitCode == 0)
			{
				notify(output is null ? $"Hook '{config.Name}' completed." : $"Hook '{config.Name}': {output}");
				return;
			}

			notify(output is null
				? $"Hook '{config.Name}' failed with exit code {process.ExitCode}."
				: $"Hook '{config.Name}' failed with exit code {process.ExitCode}: {output}");
		}
		catch (Exception exception)
		{
			notify($"Hook '{config.Name}' failed: {exception.Message}");
		}
	}

	private static void PlayAlarm(ThresholdConfig config, Action<string> notify)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				for (var index = 0; index < AlarmRepeatCount; index++)
				{
					Console.Beep(AlarmFrequency, AlarmDurationMs);
					if (index + 1 < AlarmRepeatCount)
					{
						Thread.Sleep(AlarmDelayMs);
					}
				}
			}
			else
			{
				Console.Write("\a");
			}

			notify($"Alarm played for '{config.Name}'.");
		}
		catch (Exception exception)
		{
			notify($"Alarm for '{config.Name}' failed: {exception.Message}");
		}
	}

	private static ProcessStartInfo CreateStartInfo(string command, ThresholdConfig config, TimeSpan elapsed, string configPath)
	{
		var startInfo = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe", $"/c {command}")
			: new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");

		startInfo.UseShellExecute = false;
		startInfo.CreateNoWindow = true;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		startInfo.Environment["ACTIVITY_WATCHDOG_THRESHOLD"] = config.Name;
		startInfo.Environment["ACTIVITY_WATCHDOG_ELAPSED"] = elapsed.ToString();
		startInfo.Environment["ACTIVITY_WATCHDOG_CONFIG"] = configPath;
		startInfo.Environment["ACTIVITY_WATCHDOG_TRIGGERED_AT"] = DateTimeOffset.Now.ToString("O");
		return startInfo;
	}

	private static string? FirstNonEmptyLine(string output)
	{
		if (string.IsNullOrWhiteSpace(output))
		{
			return null;
		}

		foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (!string.IsNullOrWhiteSpace(line))
			{
				return line;
			}
		}

		return null;
	}
}

internal readonly record struct ThresholdActionDispatch(bool CommandQueued, bool AlarmQueued, bool BannerQueued = false);
