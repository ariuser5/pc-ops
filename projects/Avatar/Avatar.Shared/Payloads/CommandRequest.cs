namespace Avatar.Shared.Payloads;

public sealed class CommandRequest
{
	private static readonly string[] SupportedActions =
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

	public string? Action { get; init; }

	public int? Delta { get; init; }

	public string? Key { get; init; }

	public string[]? Keys { get; init; }

	public string? Text { get; init; }

	public int? X { get; init; }

	public int? Y { get; init; }

	public string GetNormalizedAction()
	{
		if (string.IsNullOrWhiteSpace(Action))
		{
			throw new ArgumentException("Action is required.", nameof(Action));
		}

		return Action.Trim();
	}

	public Dictionary<string, string[]> Validate()
	{
		var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var normalizedAction = Action?.Trim();

		if (string.IsNullOrWhiteSpace(normalizedAction))
		{
			AddError(errors, "action", "Action is required.");
			return ToValidationErrors(errors);
		}

		if (!SupportedActions.Contains(normalizedAction, StringComparer.OrdinalIgnoreCase))
		{
			AddError(errors, "action", $"Unsupported action '{Action}'. Supported actions: {string.Join(", ", SupportedActions)}.");
			return ToValidationErrors(errors);
		}

		switch (normalizedAction)
		{
			case "MoveMouse":
				RequireCoordinates(errors);
				break;

			case "LeftClick":
			case "RightClick":
			case "DoubleClick":
				ValidateOptionalCoordinates(errors);
				break;

			case "Scroll":
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

			case "TypeText":
				if (string.IsNullOrWhiteSpace(Text))
				{
					AddError(errors, "text", "Text is required for TypeText.");
				}
				break;

			case "PressKey":
				if (string.IsNullOrWhiteSpace(Key))
				{
					AddError(errors, "key", "Key is required for PressKey.");
				}
				break;

			case "HotKey":
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