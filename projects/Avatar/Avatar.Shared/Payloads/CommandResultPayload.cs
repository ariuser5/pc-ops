using Avatar.Shared.Protocol;

namespace Avatar.Shared.Payloads;

public sealed class CommandResultPayload
{
	public double ElapsedMs { get; init; }

	public string? Message { get; init; }

	public string Status { get; init; } = CommandResultStatus.Ok.ToProtocolValue();

	public static CommandResultPayload FromStatus(CommandResultStatus status, double elapsedMs, string? message)
	{
		return new CommandResultPayload
		{
			Status = status.ToProtocolValue(),
			ElapsedMs = elapsedMs,
			Message = message
		};
	}

	public bool TryGetStatus(out CommandResultStatus status, out string error)
	{
		if (CommandResultStatusExtensions.TryParseProtocolValue(Status, out status))
		{
			error = string.Empty;
			return true;
		}

		error = $"Unsupported result status '{Status}'.";
		return false;
	}
}