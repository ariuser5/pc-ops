namespace Avatar.Shared.Payloads;

public sealed class ErrorPayload
{
	public required string Message { get; init; }
}