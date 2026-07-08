using AvatarController.Services;

namespace AvatarController.Configuration;

public static class AvatarControllerHostSetup
{
	public static AvatarControllerOptions AddAvatarControllerHost(this WebApplicationBuilder builder, string[] args)
	{
		var options = AvatarControllerOptions.Create(builder.Configuration);

		builder.WebHost.UseUrls(options.Urls);
		ConfigureLogging(builder.Logging, builder.Configuration, args);

		builder.Services.AddSingleton(options);
		builder.Services.AddSingleton<AgentManager>();
		builder.Services.AddSingleton<IAgentCommandService, AgentCommandService>();
		builder.Services.AddSingleton<AgentConnectionHandler>();
		builder.Services.AddHostedService<AgentHeartbeatService>();

		return options;
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
		// Prefer explicit command-line flags. Configuration should use the standard
		// Microsoft.Extensions.Logging configuration section (Logging:LogLevel).
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