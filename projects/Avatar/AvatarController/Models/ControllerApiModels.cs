using Avatar.Shared.Payloads;
using AvatarController.Services;

namespace AvatarController.Models;

public sealed record ControllerHealthResponse(
	string Status,
	DateTimeOffset UtcNow,
	int ConnectedAgents,
	int HeartbeatIntervalSeconds,
	int CommandTimeoutSeconds);

public sealed record AgentSummaryResponse(
	string AgentId,
	string Hostname,
	string Version,
	DateTimeOffset ConnectedSince,
	DateTimeOffset LastHeartbeat)
{
	public static AgentSummaryResponse FromSession(AgentSession session)
	{
		return new AgentSummaryResponse(
			session.AgentId,
			session.Hostname,
			session.Version,
			session.ConnectedAt,
			session.LastHeartbeatAt);
	}
}

public sealed class SendCommandApiRequest
{
	public string? AgentId { get; init; }

	public CommandRequest? Command { get; init; }
}

public sealed record SendCommandApiResponse(
	string AgentId,
	string RequestId,
	string Status,
	double ElapsedMs,
	string? Message)
{
	public static SendCommandApiResponse FromCompletion(string agentId, AgentCommandCompletion completion)
	{
		ArgumentNullException.ThrowIfNull(completion.Result);

		return new SendCommandApiResponse(
			agentId,
			completion.RequestId,
			completion.Result.Status,
			completion.Result.ElapsedMs,
			completion.Result.Message);
	}
}