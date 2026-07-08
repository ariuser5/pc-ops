using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AvatarAgent.Win32;

public sealed class MouseController
{
	private readonly ILogger<MouseController> _logger;

	public MouseController(ILogger<MouseController> logger)
	{
		_logger = logger;
	}

	public void DoubleClick()
	{
		_logger.LogDebug("Executing mouse double click.");
		NativeInput.Send(
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftDown),
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftUp),
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftDown),
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftUp));
	}

	public void LeftClick()
	{
		_logger.LogDebug("Executing mouse left click.");
		NativeInput.Send(
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftDown),
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.LeftUp));
	}

	public void Move(int x, int y)
	{
		_logger.LogDebug("Moving mouse to {X}, {Y}.", x, y);
		NativeInput.SetCursorPosition(x, y);
	}

	public void RightClick()
	{
		_logger.LogDebug("Executing mouse right click.");
		NativeInput.Send(
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.RightDown),
			NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.RightUp));
	}

	public void Scroll(int delta)
	{
		_logger.LogDebug("Scrolling mouse wheel by delta {Delta}.", delta);
		NativeInput.Send(NativeInput.CreateMouseInput(NativeInput.MouseEventFlags.Wheel, delta));
	}
}

internal static class NativeInput
{
	internal static class KeyboardEventFlags
	{
		public const uint ExtendedKey = 0x0001;
		public const uint KeyUp = 0x0002;
		public const uint Unicode = 0x0004;
	}

	internal static class MouseEventFlags
	{
		public const uint LeftDown = 0x0002;
		public const uint LeftUp = 0x0004;
		public const uint RightDown = 0x0008;
		public const uint RightUp = 0x0010;
		public const uint Wheel = 0x0800;
	}

	private const int InputKeyboard = 1;
	private const int InputMouse = 0;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int inputSize);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern short VkKeyScan(char character);

	public static INPUT CreateMouseInput(uint flags, int mouseData = 0)
	{
		return new INPUT
		{
			Type = InputMouse,
			Data = new InputUnion
			{
				Mouse = new MOUSEINPUT
				{
					Dx = 0,
					Dy = 0,
					MouseData = unchecked((uint)mouseData),
					Flags = flags,
					Time = 0,
					ExtraInfo = IntPtr.Zero
				}
			}
		};
	}

	public static INPUT CreateUnicodeKeyboardInput(char character, bool keyUp)
	{
		return new INPUT
		{
			Type = InputKeyboard,
			Data = new InputUnion
			{
				Keyboard = new KEYBDINPUT
				{
					VirtualKey = 0,
					ScanCode = character,
					Flags = KeyboardEventFlags.Unicode | (keyUp ? KeyboardEventFlags.KeyUp : 0),
					Time = 0,
					ExtraInfo = IntPtr.Zero
				}
			}
		};
	}

	public static INPUT CreateVirtualKeyKeyboardInput(ushort virtualKey, uint flags = 0)
	{
		return new INPUT
		{
			Type = InputKeyboard,
			Data = new InputUnion
			{
				Keyboard = new KEYBDINPUT
				{
					VirtualKey = virtualKey,
					ScanCode = 0,
					Flags = flags,
					Time = 0,
					ExtraInfo = IntPtr.Zero
				}
			}
		};
	}

	public static void Send(params INPUT[] inputs)
	{
		if (inputs.Length == 0)
		{
			return;
		}

		var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
		if (sent == inputs.Length)
		{
			return;
		}

		throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed.");
	}

	public static void SetCursorPosition(int x, int y)
	{
		if (!SetCursorPos(x, y))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed.");
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct INPUT
	{
		public int Type;
		public InputUnion Data;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct InputUnion
	{
		[FieldOffset(0)]
		public KEYBDINPUT Keyboard;

		[FieldOffset(0)]
		public MOUSEINPUT Mouse;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct KEYBDINPUT
	{
		public ushort VirtualKey;
		public ushort ScanCode;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MOUSEINPUT
	{
		public int Dx;
		public int Dy;
		public uint MouseData;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
	}
}