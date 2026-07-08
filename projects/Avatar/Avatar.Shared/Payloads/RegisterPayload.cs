namespace Avatar.Shared.Payloads;

public sealed class RegisterPayload
{
	public required string AgentId { get; init; }

	public required string Hostname { get; init; }

	public required string Version { get; init; }
}