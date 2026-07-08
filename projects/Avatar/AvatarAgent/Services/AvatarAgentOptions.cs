using System.Reflection;

namespace AvatarAgent.Services;

public sealed class AvatarAgentOptions
{
	public string AgentId { get; init; } = Environment.MachineName;

	public string ControllerUrl { get; init; } = "ws://127.0.0.1:5050/ws";

	public string Hostname { get; init; } = Environment.MachineName;

	public int ReconnectDelaySeconds { get; init; } = 5;

	public string Version { get; init; } = "0.1.0";

	public int WebSocketKeepAliveIntervalSeconds { get; init; } = 30;

	public TimeSpan WebSocketKeepAliveInterval => TimeSpan.FromSeconds(WebSocketKeepAliveIntervalSeconds);

	public static AvatarAgentOptions Create(IConfiguration configuration)
	{
		var section = configuration.GetSection("AvatarAgent");

		return new AvatarAgentOptions
		{
			AgentId = GetFirstNonEmpty(
				Environment.GetEnvironmentVariable("AVATAR_AGENT_ID"),
				section["AgentId"])
				?? Environment.MachineName,
			Hostname = GetFirstNonEmpty(
				Environment.GetEnvironmentVariable("AVATAR_AGENT_HOSTNAME"),
				section["Hostname"])
				?? Environment.MachineName,
			ControllerUrl = GetFirstNonEmpty(
				Environment.GetEnvironmentVariable("AVATAR_CONTROLLER_WS_URL"),
				section["ControllerUrl"])
				?? "ws://127.0.0.1:5050/ws",
			Version = GetFirstNonEmpty(
				Environment.GetEnvironmentVariable("AVATAR_AGENT_VERSION"),
				section["Version"])
				?? GetDefaultVersion(),
			ReconnectDelaySeconds = GetPositiveInt("AVATAR_AGENT_RECONNECT_SECONDS", section["ReconnectDelaySeconds"], 5),
			WebSocketKeepAliveIntervalSeconds = GetPositiveInt("AVATAR_AGENT_WS_KEEPALIVE_SECONDS", section["WebSocketKeepAliveIntervalSeconds"], 30)
		};
	}

	private static string GetDefaultVersion()
	{
		return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0";
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