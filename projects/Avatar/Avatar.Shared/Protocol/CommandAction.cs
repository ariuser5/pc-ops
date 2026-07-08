namespace Avatar.Shared.Protocol;

public enum CommandAction
{
	MoveMouse,
	LeftClick,
	RightClick,
	DoubleClick,
	Scroll,
	TypeText,
	PressKey,
	HotKey
}

public static class CommandActionExtensions
{
	private static readonly string[] SupportedProtocolValues =
	[
		"MoveMouse",
		"LeftClick",
		"RightClick",
		"DoubleClick",
		"Scroll",
		"TypeText",
		"PressKey",
		"HotKey"
	];

	public static IReadOnlyList<string> GetSupportedProtocolValues()
	{
		return SupportedProtocolValues;
	}

	public static CommandAction ParseProtocolValue(string rawValue)
	{
		if (TryParseProtocolValue(rawValue, out var action))
		{
			return action;
		}

		throw new ArgumentOutOfRangeException(nameof(rawValue), rawValue, "Unsupported command action.");
	}

	public static string ToProtocolValue(this CommandAction action)
	{
		return action switch
		{
			CommandAction.MoveMouse => "MoveMouse",
			CommandAction.LeftClick => "LeftClick",
			CommandAction.RightClick => "RightClick",
			CommandAction.DoubleClick => "DoubleClick",
			CommandAction.Scroll => "Scroll",
			CommandAction.TypeText => "TypeText",
			CommandAction.PressKey => "PressKey",
			CommandAction.HotKey => "HotKey",
			_ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported command action.")
		};
	}

	public static bool TryParseProtocolValue(string? rawValue, out CommandAction action)
	{
		action = default;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}

		switch (rawValue.Trim())
		{
			case "MoveMouse":
				action = CommandAction.MoveMouse;
				return true;
			case "LeftClick":
				action = CommandAction.LeftClick;
				return true;
			case "RightClick":
				action = CommandAction.RightClick;
				return true;
			case "DoubleClick":
				action = CommandAction.DoubleClick;
				return true;
			case "Scroll":
				action = CommandAction.Scroll;
				return true;
			case "TypeText":
				action = CommandAction.TypeText;
				return true;
			case "PressKey":
				action = CommandAction.PressKey;
				return true;
			case "HotKey":
				action = CommandAction.HotKey;
				return true;
			default:
				return false;
		}
	}
}