using System.Text.Json;

namespace Avatar.Shared.Protocol;

public sealed class AvatarEnvelope
{
	public JsonElement? Payload { get; init; }

	public string? RequestId { get; init; }

	public required string Type { get; init; }
}