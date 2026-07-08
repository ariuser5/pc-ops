using Avatar.Shared.Protocol;

namespace Avatar.Shared.Payloads;

public sealed class CommandRequest
{
	private static readonly string[] SupportedActions = CommandActionExtensions.GetSupportedProtocolValues().ToArray();

	public string? Action { get; init; }

	public int? Delta { get; init; }

	public string? Key { get; init; }

	public string[]? Keys { get; init; }

	public string? Text { get; init; }

	public int? X { get; init; }

	public int? Y { get; init; }

	public CommandAction GetRequiredAction()
	{
		if (TryGetAction(out var action, out var error))
		{
			return action;
		}

		throw new ArgumentException(error, nameof(Action));
	}

	public bool TryGetAction(out CommandAction action, out string error)
	{
		action = default;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(Action))
		{
			error = "Action is required.";
			return false;
		}

		if (!CommandActionExtensions.TryParseProtocolValue(Action, out action))
		{
			error = $"Unsupported action '{Action}'. Supported actions: {string.Join(", ", SupportedActions)}.";
			return false;
		}

		return true;
	}

	public Dictionary<string, string[]> Validate()
	{
		var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		if (!TryGetAction(out var action, out var actionError))
		{
			AddError(errors, "action", actionError);
			return ToValidationErrors(errors);
		}

		switch (action)
		{
			case CommandAction.MoveMouse:
				RequireCoordinates(errors);
				break;

			case CommandAction.LeftClick:
			case CommandAction.RightClick:
			case CommandAction.DoubleClick:
				ValidateOptionalCoordinates(errors);
				break;

			case CommandAction.Scroll:
				ValidateOptionalCoordinates(errors);
				if (!Delta.HasValue)
				{
					AddError(errors, "delta", "Delta is required for Scroll.");
				}
				else if (Delta.Value == 0)
				{
					AddError(errors, "delta", "Delta must be non-zero for Scroll.");
				}
				break;

			case CommandAction.TypeText:
				if (string.IsNullOrWhiteSpace(Text))
				{
					AddError(errors, "text", "Text is required for TypeText.");
				}
				break;

			case CommandAction.PressKey:
				if (string.IsNullOrWhiteSpace(Key))
				{
					AddError(errors, "key", "Key is required for PressKey.");
				}
				break;

			case CommandAction.HotKey:
				if (Keys is null || Keys.Length == 0)
				{
					AddError(errors, "keys", "Keys must contain at least one value for HotKey.");
				}
				else if (Keys.Any(static key => string.IsNullOrWhiteSpace(key)))
				{
					AddError(errors, "keys", "Keys cannot contain empty values.");
				}
				break;
		}

		return ToValidationErrors(errors);
	}

	private void RequireCoordinates(Dictionary<string, List<string>> errors)
	{
		if (!X.HasValue)
		{
			AddError(errors, "x", "X is required for MoveMouse.");
		}

		if (!Y.HasValue)
		{
			AddError(errors, "y", "Y is required for MoveMouse.");
		}
	}

	private void ValidateOptionalCoordinates(Dictionary<string, List<string>> errors)
	{
		if (X.HasValue ^ Y.HasValue)
		{
			AddError(errors, "x", "X and Y must both be provided when specifying coordinates.");
			AddError(errors, "y", "X and Y must both be provided when specifying coordinates.");
		}
	}

	private static void AddError(Dictionary<string, List<string>> errors, string fieldName, string message)
	{
		if (!errors.TryGetValue(fieldName, out var fieldErrors))
		{
			fieldErrors = [];
			errors[fieldName] = fieldErrors;
		}

		fieldErrors.Add(message);
	}

	private static Dictionary<string, string[]> ToValidationErrors(Dictionary<string, List<string>> errors)
	{
		return errors.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
	}
}