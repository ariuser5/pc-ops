using System.Reflection;

namespace AvatarAgent.Services;

public sealed class AvatarAgentOptions
{
	public string AgentId { get; init; } = Environment.MachineName;

	public string ControllerUrl { get; init; } = "ws://127.0.0.1:5050/ws";

	public string Hostname { get; init; } = Environment.MachineName;

	public int ReconnectDelaySeconds { get; init; } = 5;

	public string Version { get; init; } = "0.1.0";

	public static AvatarAgentOptions Create(IConfiguration configuration)
	{
		var reconnectDelaySeconds = 5;
		var reconnectRaw = Environment.GetEnvironmentVariable("AVATAR_AGENT_RECONNECT_SECONDS")
			?? configuration["AvatarAgent:ReconnectDelaySeconds"];
		if (int.TryParse(reconnectRaw, out var parsedDelay) && parsedDelay > 0)
		{
			reconnectDelaySeconds = parsedDelay;
		}

		return new AvatarAgentOptions
		{
			AgentId = Environment.GetEnvironmentVariable("AVATAR_AGENT_ID")
				?? configuration["AvatarAgent:AgentId"]
				?? Environment.MachineName,
			Hostname = Environment.GetEnvironmentVariable("AVATAR_AGENT_HOSTNAME")
				?? configuration["AvatarAgent:Hostname"]
				?? Environment.MachineName,
			ControllerUrl = Environment.GetEnvironmentVariable("AVATAR_CONTROLLER_WS_URL")
				?? configuration["AvatarAgent:ControllerUrl"]
				?? "ws://127.0.0.1:5050/ws",
			Version = Environment.GetEnvironmentVariable("AVATAR_AGENT_VERSION")
				?? configuration["AvatarAgent:Version"]
				?? GetDefaultVersion(),
			ReconnectDelaySeconds = reconnectDelaySeconds
		};
	}

	private static string GetDefaultVersion()
	{
		return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0";
	}
}