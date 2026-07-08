using AvatarAgent.Win32;

namespace AvatarAgent.Services;

public static class AvatarAgentHostSetup
{
	public static void AddAvatarAgentHost(this HostApplicationBuilder builder, string[] args)
	{
		ConfigureLogging(builder.Logging, builder.Configuration, args);

		builder.Services.AddSingleton<MouseController>();
		builder.Services.AddSingleton<KeyboardController>();
		builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
		builder.Services.AddSingleton<AvatarAgentOptions>(sp => AvatarAgentOptions.Create(sp.GetRequiredService<IConfiguration>()));
		builder.Services.AddHostedService<ControllerConnectionService>();
	}

	private static void ConfigureLogging(ILoggingBuilder logging, IConfiguration configuration, string[] args)
	{
		logging.ClearProviders();
		logging.AddConfiguration(configuration.GetSection("Logging"));
		logging.AddConsole();

		var minimumLogLevel = ResolveMinimumLogLevel(configuration, args);
		if (minimumLogLevel.HasValue)
		{
			logging.SetMinimumLevel(minimumLogLevel.Value);
		}
	}

	private static string? FindCommandLineValue(string[] args, string optionName)
	{
		for (var index = 0; index < args.Length; index++)
		{
			var argument = args[index];
			if (argument.Equals(optionName, StringComparison.OrdinalIgnoreCase))
			{
				return index + 1 < args.Length ? args[index + 1] : null;
			}

			var prefix = optionName + "=";
			if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return argument[prefix.Length..];
			}
		}

		return null;
	}

	private static LogLevel ParseLogLevel(string rawValue)
	{
		if (Enum.TryParse<LogLevel>(rawValue, ignoreCase: true, out var logLevel))
		{
			return logLevel;
		}

		throw new ArgumentOutOfRangeException(nameof(rawValue), rawValue, "Supported log levels are Trace, Debug, Information, Warning, Error, Critical, and None.");
	}

	private static LogLevel? ResolveMinimumLogLevel(IConfiguration configuration, string[] args)
	{
		// Prefer command-line flags for quick overrides. Use the standard
		// Logging:LogLevel configuration for persistent settings.
		var rawLogLevel = FindCommandLineValue(args, "--log-level");

		if (!string.IsNullOrWhiteSpace(rawLogLevel))
		{
			return ParseLogLevel(rawLogLevel);
		}

		if (args.Any(static argument => argument.Equals("--verbose", StringComparison.OrdinalIgnoreCase)))
		{
			return LogLevel.Trace;
		}

		return null;
	}
}