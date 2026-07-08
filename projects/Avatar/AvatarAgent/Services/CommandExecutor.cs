using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;
using AvatarAgent.Win32;

namespace AvatarAgent.Services;

public sealed class CommandExecutor : ICommandExecutor
{
	private readonly KeyboardController _keyboardController;
	private readonly ILogger<CommandExecutor> _logger;
	private readonly MouseController _mouseController;

	public CommandExecutor(MouseController mouseController, KeyboardController keyboardController, ILogger<CommandExecutor> logger)
	{
		_mouseController = mouseController;
		_keyboardController = keyboardController;
		_logger = logger;
	}

	public Task<CommandExecutionResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var action = request.GetRequiredAction();
		_logger.LogInformation("Executing action {Action}.", action.ToProtocolValue());

		var result = action switch
		{
			CommandAction.MoveMouse => ExecuteMoveMouse(request),
			CommandAction.LeftClick => ExecuteLeftClick(request),
			CommandAction.RightClick => ExecuteRightClick(request),
			CommandAction.DoubleClick => ExecuteDoubleClick(request),
			CommandAction.Scroll => ExecuteScroll(request),
			CommandAction.TypeText => ExecuteTypeText(request),
			CommandAction.PressKey => ExecutePressKey(request),
			CommandAction.HotKey => ExecuteHotKey(request),
			_ => throw new ArgumentException($"Unsupported action '{action}'.", nameof(request))
		};

		return Task.FromResult(result);
	}

	private CommandExecutionResult ExecuteDoubleClick(CommandRequest request)
	{
		MoveMouseIfRequested(request);
		_mouseController.DoubleClick();
		return new CommandExecutionResult("DoubleClick", "Double click executed.");
	}

	private CommandExecutionResult ExecuteHotKey(CommandRequest request)
	{
		var keys = request.Keys ?? throw new ArgumentException("Keys are required for HotKey.", nameof(request));
		_keyboardController.HotKey(keys);
		return new CommandExecutionResult("HotKey", $"HotKey executed with {keys.Length} key(s).");
	}

	private CommandExecutionResult ExecuteLeftClick(CommandRequest request)
	{
		MoveMouseIfRequested(request);
		_mouseController.LeftClick();
		return new CommandExecutionResult("LeftClick", "Left click executed.");
	}

	private CommandExecutionResult ExecuteMoveMouse(CommandRequest request)
	{
		var x = request.X ?? throw new ArgumentException("X is required for MoveMouse.", nameof(request));
		var y = request.Y ?? throw new ArgumentException("Y is required for MoveMouse.", nameof(request));
		_mouseController.Move(x, y);
		return new CommandExecutionResult("MoveMouse", $"Mouse moved to ({x}, {y}).");
	}

	private CommandExecutionResult ExecutePressKey(CommandRequest request)
	{
		var key = request.Key ?? throw new ArgumentException("Key is required for PressKey.", nameof(request));
		_keyboardController.PressKey(key);
		return new CommandExecutionResult("PressKey", $"Key '{key}' pressed.");
	}

	private CommandExecutionResult ExecuteRightClick(CommandRequest request)
	{
		MoveMouseIfRequested(request);
		_mouseController.RightClick();
		return new CommandExecutionResult("RightClick", "Right click executed.");
	}

	private CommandExecutionResult ExecuteScroll(CommandRequest request)
	{
		MoveMouseIfRequested(request);
		var delta = request.Delta ?? throw new ArgumentException("Delta is required for Scroll.", nameof(request));
		_mouseController.Scroll(delta);
		return new CommandExecutionResult("Scroll", $"Scrolled by delta {delta}.");
	}

	private CommandExecutionResult ExecuteTypeText(CommandRequest request)
	{
		var text = request.Text ?? throw new ArgumentException("Text is required for TypeText.", nameof(request));
		_keyboardController.TypeText(text);
		return new CommandExecutionResult("TypeText", $"Typed {text.Length} character(s).");
	}

	private void MoveMouseIfRequested(CommandRequest request)
	{
		if (request.X.HasValue && request.Y.HasValue)
		{
			_mouseController.Move(request.X.Value, request.Y.Value);
		}
	}
}