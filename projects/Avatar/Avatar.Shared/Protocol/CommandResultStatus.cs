namespace Avatar.Shared.Protocol;

public enum CommandResultStatus
{
	Ok,
	Error
}

public static class CommandResultStatusExtensions
{
	public static string ToProtocolValue(this CommandResultStatus status)
	{
		return status switch
		{
			CommandResultStatus.Ok => "ok",
			CommandResultStatus.Error => "error",
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported result status.")
		};
	}
}