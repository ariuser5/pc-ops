using System.Collections.ObjectModel;

namespace AvatarAgent.Win32;

public sealed class KeyboardController
{
	private static readonly IReadOnlyDictionary<string, KeyDefinition> KnownKeys = new ReadOnlyDictionary<string, KeyDefinition>(
		new Dictionary<string, KeyDefinition>(StringComparer.OrdinalIgnoreCase)
		{
			["Alt"] = new(0x12),
			["Apps"] = new(0x5D, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Backspace"] = new(0x08),
			["Ctrl"] = new(0x11),
			["Control"] = new(0x11),
			["Delete"] = new(0x2E, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Down"] = new(0x28, NativeInput.KeyboardEventFlags.ExtendedKey),
			["End"] = new(0x23, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Enter"] = new(0x0D),
			["Esc"] = new(0x1B),
			["Escape"] = new(0x1B),
			["Home"] = new(0x24, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Insert"] = new(0x2D, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Left"] = new(0x25, NativeInput.KeyboardEventFlags.ExtendedKey),
			["LWin"] = new(0x5B, NativeInput.KeyboardEventFlags.ExtendedKey),
			["PageDown"] = new(0x22, NativeInput.KeyboardEventFlags.ExtendedKey),
			["PageUp"] = new(0x21, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Right"] = new(0x27, NativeInput.KeyboardEventFlags.ExtendedKey),
			["RWin"] = new(0x5C, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Shift"] = new(0x10),
			["Space"] = new(0x20),
			["Tab"] = new(0x09),
			["Up"] = new(0x26, NativeInput.KeyboardEventFlags.ExtendedKey),
			["Win"] = new(0x5B, NativeInput.KeyboardEventFlags.ExtendedKey)
		});

	private readonly ILogger<KeyboardController> _logger;

	public KeyboardController(ILogger<KeyboardController> logger)
	{
		_logger = logger;
	}

	public void HotKey(IEnumerable<string> keys)
	{
		var resolvedKeys = keys.Select(ResolveKey).ToList();
		if (resolvedKeys.Count == 0)
		{
			throw new ArgumentException("At least one key is required for HotKey.", nameof(keys));
		}

		_logger.LogDebug("Executing hotkey with {KeyCount} key(s).", resolvedKeys.Count);

		var inputs = new List<NativeInput.INPUT>(resolvedKeys.Count * 2);
		foreach (var key in resolvedKeys)
		{
			inputs.Add(NativeInput.CreateVirtualKeyKeyboardInput(key.VirtualKey, key.Flags));
		}

		for (var index = resolvedKeys.Count - 1; index >= 0; index--)
		{
			var key = resolvedKeys[index];
			inputs.Add(NativeInput.CreateVirtualKeyKeyboardInput(key.VirtualKey, key.Flags | NativeInput.KeyboardEventFlags.KeyUp));
		}

		NativeInput.Send(inputs.ToArray());
	}

	public void PressKey(string key)
	{
		var resolvedKey = ResolveKey(key);
		_logger.LogDebug("Pressing key {Key}.", key);
		NativeInput.Send(
			NativeInput.CreateVirtualKeyKeyboardInput(resolvedKey.VirtualKey, resolvedKey.Flags),
			NativeInput.CreateVirtualKeyKeyboardInput(resolvedKey.VirtualKey, resolvedKey.Flags | NativeInput.KeyboardEventFlags.KeyUp));
	}

	public void TypeText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		_logger.LogDebug("Typing {CharacterCount} character(s).", text.Length);

		var inputs = new List<NativeInput.INPUT>(text.Length * 2);
		foreach (var character in text)
		{
			inputs.Add(NativeInput.CreateUnicodeKeyboardInput(character, keyUp: false));
			inputs.Add(NativeInput.CreateUnicodeKeyboardInput(character, keyUp: true));
		}

		NativeInput.Send(inputs.ToArray());
	}

	private static KeyDefinition ResolveKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException("Key cannot be empty.", nameof(key));
		}

		var normalizedKey = key.Trim();
		if (KnownKeys.TryGetValue(normalizedKey, out var knownKey))
		{
			return knownKey;
		}

		if (TryResolveFunctionKey(normalizedKey, out var functionKey))
		{
			return functionKey;
		}

		if (normalizedKey.Length == 1)
		{
			var character = normalizedKey[0];
			if (char.IsLetter(character))
			{
				return new KeyDefinition((ushort)char.ToUpperInvariant(character));
			}

			if (char.IsDigit(character))
			{
				return new KeyDefinition((ushort)character);
			}

			var vk = NativeInput.VkKeyScan(character);
			if (vk >= 0)
			{
				return new KeyDefinition((ushort)(vk & 0xFF));
			}
		}

		throw new ArgumentException($"Unsupported key '{key}'.", nameof(key));
	}

	private static bool TryResolveFunctionKey(string key, out KeyDefinition functionKey)
	{
		functionKey = default;
		if (!key.StartsWith("F", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!int.TryParse(key[1..], out var functionKeyNumber) || functionKeyNumber is < 1 or > 24)
		{
			return false;
		}

		functionKey = new KeyDefinition((ushort)(0x70 + functionKeyNumber - 1));
		return true;
	}

	private readonly record struct KeyDefinition(ushort VirtualKey, uint Flags = 0);
}