namespace Avatar.Shared.Protocol;

public enum CommandResultStatus
{
	Ok,
	Error
}

public static class CommandResultStatusExtensions
{
	public static CommandResultStatus ParseProtocolValue(string rawValue)
	{
		if (TryParseProtocolValue(rawValue, out var status))
		{
			return status;
		}

		throw new ArgumentOutOfRangeException(nameof(rawValue), rawValue, "Unsupported result status.");
	}

	public static string ToProtocolValue(this CommandResultStatus status)
	{
		return status switch
		{
			CommandResultStatus.Ok => "ok",
			CommandResultStatus.Error => "error",
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported result status.")
		};
	}

	public static bool TryParseProtocolValue(string? rawValue, out CommandResultStatus status)
	{
		status = default;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}

		switch (rawValue.Trim().ToLowerInvariant())
		{
			case "ok":
				status = CommandResultStatus.Ok;
				return true;
			case "error":
				status = CommandResultStatus.Error;
				return true;
			default:
				return false;
		}
	}
}