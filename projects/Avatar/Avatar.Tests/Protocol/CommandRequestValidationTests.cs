using Avatar.Shared.Payloads;

namespace Avatar.Tests.Protocol;

public sealed class CommandRequestValidationTests
{
	// ── MoveMouse ───────────────────────────────────────────────────────────

	[Fact]
	public void MoveMouse_ValidCoordinates_NoErrors()
	{
		var request = new CommandRequest { Action = "MoveMouse", X = 100, Y = 200 };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void MoveMouse_MissingX_HasError()
	{
		var request = new CommandRequest { Action = "MoveMouse", Y = 200 };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("x"));
	}

	[Fact]
	public void MoveMouse_MissingY_HasError()
	{
		var request = new CommandRequest { Action = "MoveMouse", X = 100 };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("y"));
	}

	[Fact]
	public void MoveMouse_MissingBothCoordinates_HasBothErrors()
	{
		var request = new CommandRequest { Action = "MoveMouse" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("x"));
		Assert.True(errors.ContainsKey("y"));
	}

	// ── Click actions (optional coordinates) ────────────────────────────────

	[Theory]
	[InlineData("LeftClick")]
	[InlineData("RightClick")]
	[InlineData("DoubleClick")]
	public void ClickActions_NoCoordinates_NoErrors(string action)
	{
		var request = new CommandRequest { Action = action };
		Assert.Empty(request.Validate());
	}

	[Theory]
	[InlineData("LeftClick")]
	[InlineData("RightClick")]
	[InlineData("DoubleClick")]
	public void ClickActions_BothCoordinates_NoErrors(string action)
	{
		var request = new CommandRequest { Action = action, X = 10, Y = 20 };
		Assert.Empty(request.Validate());
	}

	[Theory]
	[InlineData("LeftClick")]
	[InlineData("RightClick")]
	[InlineData("DoubleClick")]
	public void ClickActions_OnlyX_HasErrors(string action)
	{
		var request = new CommandRequest { Action = action, X = 10 };
		var errors = request.Validate();
		Assert.NotEmpty(errors);
	}

	[Theory]
	[InlineData("LeftClick")]
	[InlineData("RightClick")]
	[InlineData("DoubleClick")]
	public void ClickActions_OnlyY_HasErrors(string action)
	{
		var request = new CommandRequest { Action = action, Y = 20 };
		var errors = request.Validate();
		Assert.NotEmpty(errors);
	}

	// ── Scroll ───────────────────────────────────────────────────────────────

	[Fact]
	public void Scroll_WithDelta_NoErrors()
	{
		var request = new CommandRequest { Action = "Scroll", Delta = 3 };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void Scroll_WithDeltaAndCoordinates_NoErrors()
	{
		var request = new CommandRequest { Action = "Scroll", Delta = -2, X = 50, Y = 60 };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void Scroll_NoDelta_HasError()
	{
		var request = new CommandRequest { Action = "Scroll" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("delta"));
	}

	[Fact]
	public void Scroll_ZeroDelta_HasError()
	{
		var request = new CommandRequest { Action = "Scroll", Delta = 0 };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("delta"));
	}

	// ── TypeText ─────────────────────────────────────────────────────────────

	[Fact]
	public void TypeText_WithText_NoErrors()
	{
		var request = new CommandRequest { Action = "TypeText", Text = "hello" };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void TypeText_EmptyText_HasError()
	{
		var request = new CommandRequest { Action = "TypeText", Text = "" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("text"));
	}

	[Fact]
	public void TypeText_NullText_HasError()
	{
		var request = new CommandRequest { Action = "TypeText" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("text"));
	}

	// ── PressKey ─────────────────────────────────────────────────────────────

	[Fact]
	public void PressKey_WithKey_NoErrors()
	{
		var request = new CommandRequest { Action = "PressKey", Key = "Enter" };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void PressKey_NullKey_HasError()
	{
		var request = new CommandRequest { Action = "PressKey" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("key"));
	}

	// ── HotKey ───────────────────────────────────────────────────────────────

	[Fact]
	public void HotKey_WithKeys_NoErrors()
	{
		var request = new CommandRequest { Action = "HotKey", Keys = ["ctrl", "c"] };
		Assert.Empty(request.Validate());
	}

	[Fact]
	public void HotKey_EmptyKeysArray_HasError()
	{
		var request = new CommandRequest { Action = "HotKey", Keys = [] };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("keys"));
	}

	[Fact]
	public void HotKey_NullKeys_HasError()
	{
		var request = new CommandRequest { Action = "HotKey" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("keys"));
	}

	[Fact]
	public void HotKey_BlankKeyInArray_HasError()
	{
		var request = new CommandRequest { Action = "HotKey", Keys = ["ctrl", ""] };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("keys"));
	}

	// ── Invalid action ───────────────────────────────────────────────────────

	[Fact]
	public void UnknownAction_HasActionError()
	{
		var request = new CommandRequest { Action = "FlyToMoon" };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("action"));
	}

	[Fact]
	public void NullAction_HasActionError()
	{
		var request = new CommandRequest { Action = null };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("action"));
	}

	[Fact]
	public void WrongCaseAction_HasActionError()
	{
		// Actions are case-sensitive in the protocol
		var request = new CommandRequest { Action = "movemouse", X = 10, Y = 20 };
		var errors = request.Validate();
		Assert.True(errors.ContainsKey("action"));
	}
}
