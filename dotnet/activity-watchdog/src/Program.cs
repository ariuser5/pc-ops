using System.Diagnostics;
using System.Text.Json;

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
	private readonly AppConfig _config;
	private readonly string _configPath;
	private readonly object _stateLock = new();
	private readonly ThresholdState[] _thresholds;
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private readonly CancellationTokenSource _shutdown = new();

	private bool _isStopped;
	private bool _shouldExit;
	private TimeSpan _stoppedElapsed = TimeSpan.Zero;
	private DateTimeOffset _lastResetAt = DateTimeOffset.Now;
	private string _lastResetReason = "startup";
	private string _lastEventMessage = "Timer started.";

	public ActivityWatchdogApp(AppConfig config, string configPath)
	{
		_config = config;
		_configPath = configPath;
		_thresholds = config.Thresholds.Select(threshold => new ThresholdState(threshold)).ToArray();

		if (Console.IsInputRedirected)
		{
			_lastEventMessage = "Console input is redirected; keyboard controls are unavailable.";
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

			switch (keyInfo.Key)
			{
				case ConsoleKey.R:
					Reset("manual");
					break;
				case ConsoleKey.S:
					StopTimer();
					break;
				case ConsoleKey.Q:
					RequestQuit();
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
				var elapsed = GetElapsed();
				var activeThreshold = EvaluateThresholds(elapsed);

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

	private void Reset(string reason)
	{
		lock (_stateLock)
		{
			_stopwatch.Restart();
			_isStopped = false;
			_stoppedElapsed = TimeSpan.Zero;
			_lastResetAt = DateTimeOffset.Now;
			_lastResetReason = reason;
			_lastEventMessage = "Timer reset.";

			foreach (var threshold in _thresholds)
			{
				threshold.HasTriggered = false;
			}
		}
	}

	private void StopTimer()
	{
		lock (_stateLock)
		{
			if (_isStopped)
			{
				return;
			}

			_stoppedElapsed = _stopwatch.Elapsed;
			_isStopped = true;
			_lastEventMessage = $"Timer stopped at {FormatElapsed(_stoppedElapsed)}.";
		}
	}

	private void RequestQuit()
	{
		lock (_stateLock)
		{
			_lastEventMessage = "Quitting.";
			_shouldExit = true;
		}

		_shutdown.Cancel();
	}

	private TimeSpan GetElapsed()
	{
		return _isStopped ? _stoppedElapsed : _stopwatch.Elapsed;
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
			var hookQueued = ThresholdHookRunner.TryQueue(threshold.Config, elapsed, _configPath);
			var hookMessage = hookQueued ? " Hook queued." : string.Empty;
			_lastEventMessage = $"Threshold '{threshold.Config.Name}' reached at {FormatElapsed(elapsed)}.{hookMessage}";
		}

		return activeThreshold;
	}

	private void Render(TimeSpan elapsed, ThresholdState? activeThreshold)
	{
		var width = GetRenderWidth();
		var statusColor = _isStopped
			? ConsoleColor.DarkGray
			: activeThreshold?.DisplayColor ?? ConsoleColor.Green;

		var nextThreshold = _thresholds.FirstOrDefault(threshold => elapsed < threshold.Config.Duration);
		var stateLine = $"State: {(_isStopped ? "stopped" : "running")} | Last reset: {_lastResetAt:yyyy-MM-dd HH:mm:ss} ({FormatResetReason(_lastResetReason)})";

		var lines = new (string Text, ConsoleColor? Color)[]
		{
			("Activity Watchdog", ConsoleColor.Cyan),
			($"Elapsed: {FormatElapsed(elapsed)}", statusColor),
			(stateLine, null),
			($"Next threshold: {DescribeNextThreshold(nextThreshold)}", null),
			($"Last event: {_lastEventMessage}", null),
			($"Config: {_configPath}", ConsoleColor.DarkGray),
			("Controls: [R] reset  [S] stop  [Q] quit", ConsoleColor.DarkGray)
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

		Console.ResetColor();
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

	private static string FormatResetReason(string reason)
	{
		return reason switch
		{
			"manual" => "manual reset",
			_ => reason
		};
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
		Console.WriteLine("  Q  Quit");
		Console.WriteLine();
		Console.WriteLine("Notes:");
		Console.WriteLine("  Threshold commands run once per threshold until the timer is reset.");
	}
}

internal static class AppConfigLoader
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		AllowTrailingCommas = true,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip
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

	public List<ThresholdConfig> Thresholds { get; set; } = [];

	public void Normalize()
	{
		RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 100, 10_000);

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

internal sealed class ThresholdConfig
{
	public string Name { get; set; } = string.Empty;

	public TimeSpan Duration { get; set; } = TimeSpan.Zero;

	public string? Color { get; set; }

	public string? Command { get; set; }
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
	public static bool TryQueue(ThresholdConfig config, TimeSpan elapsed, string configPath)
	{
		if (string.IsNullOrWhiteSpace(config.Command))
		{
			return false;
		}

		_ = Task.Run(() => StartProcess(config, elapsed, configPath));
		return true;
	}

	private static void StartProcess(ThresholdConfig config, TimeSpan elapsed, string configPath)
	{
		try
		{
			using var process = Process.Start(CreateStartInfo(config.Command!, config, elapsed, configPath));
			process?.Dispose();
		}
		catch
		{
		}
	}

	private static ProcessStartInfo CreateStartInfo(string command, ThresholdConfig config, TimeSpan elapsed, string configPath)
	{
		var startInfo = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe", $"/c {command}")
			: new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");

		startInfo.UseShellExecute = false;
		startInfo.CreateNoWindow = true;
		startInfo.Environment["ACTIVITY_WATCHDOG_THRESHOLD"] = config.Name;
		startInfo.Environment["ACTIVITY_WATCHDOG_ELAPSED"] = elapsed.ToString();
		startInfo.Environment["ACTIVITY_WATCHDOG_CONFIG"] = configPath;
		startInfo.Environment["ACTIVITY_WATCHDOG_TRIGGERED_AT"] = DateTimeOffset.Now.ToString("O");
		return startInfo;
	}
}
