namespace AvatarController.Configuration;

public sealed class AvatarControllerOptions
{
	public int CommandTimeoutSeconds { get; init; } = 15;

	public int HeartbeatIntervalSeconds { get; init; } = 30;

	public int HeartbeatTimeoutSeconds { get; init; } = 90;

	public string Urls { get; init; } = "http://0.0.0.0:5050";

	public int WebSocketKeepAliveIntervalSeconds { get; init; } = 30;

	public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

	public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(HeartbeatIntervalSeconds);

	public TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(HeartbeatTimeoutSeconds);

	public TimeSpan WebSocketKeepAliveInterval => TimeSpan.FromSeconds(WebSocketKeepAliveIntervalSeconds);

	public static AvatarControllerOptions Create(IConfiguration configuration)
	{
		var section = configuration.GetSection("AvatarController");

		return new AvatarControllerOptions
		{
			Urls = GetFirstNonEmpty(
				Environment.GetEnvironmentVariable("AVATAR_CONTROLLER_URLS"),
				section["Urls"])
				?? "http://0.0.0.0:5050",
			HeartbeatIntervalSeconds = GetPositiveInt("AVATAR_CONTROLLER_HEARTBEAT_INTERVAL_SECONDS", section["HeartbeatIntervalSeconds"], 30),
			HeartbeatTimeoutSeconds = GetPositiveInt("AVATAR_CONTROLLER_HEARTBEAT_TIMEOUT_SECONDS", section["HeartbeatTimeoutSeconds"], 90),
			CommandTimeoutSeconds = GetPositiveInt("AVATAR_CONTROLLER_COMMAND_TIMEOUT_SECONDS", section["CommandTimeoutSeconds"], 15),
			WebSocketKeepAliveIntervalSeconds = GetPositiveInt("AVATAR_CONTROLLER_WS_KEEPALIVE_SECONDS", section["WebSocketKeepAliveIntervalSeconds"], 30)
		};
	}

	private static string? GetFirstNonEmpty(params string?[] values)
	{
		foreach (var value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}

		return null;
	}

	private static int GetPositiveInt(string environmentVariableName, string? configurationValue, int defaultValue)
	{
		var rawValue = GetFirstNonEmpty(Environment.GetEnvironmentVariable(environmentVariableName), configurationValue);
		if (int.TryParse(rawValue, out var parsedValue) && parsedValue > 0)
		{
			return parsedValue;
		}

		return defaultValue;
	}
}