using Avatar.Shared.Protocol;

namespace Avatar.Shared.Payloads;

public sealed class CommandResultPayload
{
	public double ElapsedMs { get; init; }

	public string? Message { get; init; }

	public string Status { get; init; } = CommandResultStatus.Ok.ToProtocolValue();
}