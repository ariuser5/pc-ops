namespace Avatar.Shared.Protocol;

public enum AvatarMessageType
{
	Register,
	Command,
	Result,
	Error,
	Heartbeat
}

public static class AvatarMessageTypeExtensions
{
	public static string ToProtocolValue(this AvatarMessageType messageType)
	{
		return messageType switch
		{
			AvatarMessageType.Register => "register",
			AvatarMessageType.Command => "command",
			AvatarMessageType.Result => "result",
			AvatarMessageType.Error => "error",
			AvatarMessageType.Heartbeat => "heartbeat",
			_ => throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "Unsupported message type.")
		};
	}

	public static bool TryParseProtocolValue(string? rawValue, out AvatarMessageType messageType)
	{
		messageType = default;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}

		switch (rawValue.Trim().ToLowerInvariant())
		{
			case "register":
				messageType = AvatarMessageType.Register;
				return true;
			case "command":
				messageType = AvatarMessageType.Command;
				return true;
			case "result":
				messageType = AvatarMessageType.Result;
				return true;
			case "error":
				messageType = AvatarMessageType.Error;
				return true;
			case "heartbeat":
				messageType = AvatarMessageType.Heartbeat;
				return true;
			default:
				return false;
		}
	}
}