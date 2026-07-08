using System.Diagnostics;
using System.Net.WebSockets;
using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;

namespace AvatarAgent.Services;

public sealed class ControllerConnectionService : BackgroundService
{
	private readonly ICommandExecutor _commandExecutor;
	private readonly ILogger<ControllerConnectionService> _logger;
	private readonly AvatarAgentOptions _options;

	public ControllerConnectionService(AvatarAgentOptions options, ICommandExecutor commandExecutor, ILogger<ControllerConnectionService> logger)
	{
		_options = options;
		_commandExecutor = commandExecutor;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			using var socket = new ClientWebSocket();
			socket.Options.KeepAliveInterval = _options.WebSocketKeepAliveInterval;
			try
			{
				_logger.LogInformation("Connecting to controller at {ControllerUrl}...", _options.ControllerUrl);
				await socket.ConnectAsync(new Uri(_options.ControllerUrl), stoppingToken);
				_logger.LogInformation("Connected to controller.");

				await SendRegisterAsync(socket, stoppingToken);
				await ReceiveLoopAsync(socket, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Controller connection lost.");
			}
			finally
			{
				await CloseSocketAsync(socket);
			}

			if (!stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("Reconnect attempt in {Seconds} second(s).", _options.ReconnectDelaySeconds);
				await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
			}
		}
	}

	private async Task HandleCommandAsync(ClientWebSocket socket, AvatarEnvelope envelope, CancellationToken cancellationToken)
	{
		if (!AvatarProtocolJson.TryDeserializePayload<CommandRequest>(envelope, out var command, out var parseError) || command is null)
		{
			_logger.LogWarning("Failed to deserialize command payload: {Error}", parseError);
			await SendErrorAsync(socket, envelope.RequestId, parseError, cancellationToken);
			return;
		}

		var validationErrors = command.Validate();
		if (validationErrors.Count > 0)
		{
			var firstError = validationErrors.Values.SelectMany(static entry => entry).FirstOrDefault() ?? "Invalid command payload.";
			await SendErrorAsync(socket, envelope.RequestId, firstError, cancellationToken);
			return;
		}

		var stopwatch = Stopwatch.StartNew();
		try
		{
			var executionResult = await _commandExecutor.ExecuteAsync(command, cancellationToken);
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			_logger.LogInformation("Command {Action} executed in {ElapsedMs} ms.", executionResult.Action, elapsedMs);

			await SendEnvelopeAsync(
				socket,
				AvatarProtocolJson.CreateEnvelope(
					AvatarMessageType.Result,
					envelope.RequestId,
					CommandResultPayload.FromStatus(CommandResultStatus.Ok, elapsedMs, executionResult.Message)),
				cancellationToken);
		}
		catch (ArgumentException exception)
		{
			stopwatch.Stop();
			await SendErrorAsync(socket, envelope.RequestId, exception.Message, cancellationToken);
		}
		catch (Exception exception)
		{
			stopwatch.Stop();
			_logger.LogError(exception, "Command execution failed.");
			await SendErrorAsync(socket, envelope.RequestId, "Command execution failed.", cancellationToken);
		}
	}

	private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var incoming = await WebSocketTextMessageCodec.ReceiveTextAsync(socket, cancellationToken);
			if (incoming is null)
			{
				_logger.LogWarning("Controller closed the websocket connection.");
				break;
			}

			_logger.LogDebug("Received raw message from controller: {Payload}", incoming);
			if (!AvatarProtocolJson.TryDeserializeEnvelope(incoming, out var envelope, out var parseError) || envelope is null)
			{
				_logger.LogWarning("Failed to deserialize controller envelope: {Error}", parseError);
				await SendErrorAsync(socket, null, parseError, cancellationToken);
				continue;
			}

			if (!envelope.TryGetMessageType(out var messageType))
			{
				_logger.LogWarning("Controller sent unsupported message type: {Type}", envelope.Type);
				await SendErrorAsync(socket, envelope.RequestId, $"Unsupported message type '{envelope.Type}'.", cancellationToken);
				continue;
			}

			switch (messageType)
			{
				case AvatarMessageType.Command:
					await HandleCommandAsync(socket, envelope, cancellationToken);
					break;

				case AvatarMessageType.Heartbeat:
					_logger.LogDebug("Heartbeat received from controller.");
					await SendEnvelopeAsync(
						socket,
						AvatarProtocolJson.CreateEnvelope<object?>(AvatarMessageType.Heartbeat, envelope.RequestId, null),
						cancellationToken);
					break;

				case AvatarMessageType.Error:
					if (AvatarProtocolJson.TryDeserializePayload<ErrorPayload>(envelope, out var errorPayload, out _) && errorPayload is not null)
					{
						_logger.LogWarning("Controller error: {Message}", errorPayload.Message);
					}
					break;

				default:
					_logger.LogDebug("Ignoring message type {MessageType}.", messageType);
					break;
			}
		}
	}

	private async Task SendEnvelopeAsync(ClientWebSocket socket, AvatarEnvelope envelope, CancellationToken cancellationToken)
	{
		if (socket.State != WebSocketState.Open)
		{
			return;
		}

		var json = AvatarProtocolJson.Serialize(envelope);
		await WebSocketTextMessageCodec.SendTextAsync(socket, json, cancellationToken);
		if (envelope.TryGetMessageType(out var messageType) && messageType == AvatarMessageType.Heartbeat)
		{
			_logger.LogDebug("Sent {MessageType} (requestId: {RequestId}).", envelope.Type, envelope.RequestId ?? "none");
		}
		else
		{
			_logger.LogInformation("Sent {MessageType} (requestId: {RequestId}).", envelope.Type, envelope.RequestId ?? "none");
		}
		_logger.LogDebug("Sent raw message to controller: {Payload}", json);
	}

	private Task SendErrorAsync(ClientWebSocket socket, string? requestId, string message, CancellationToken cancellationToken)
	{
		_logger.LogWarning("Sending error response for request {RequestId}: {Message}", requestId ?? "none", message);
		return SendEnvelopeAsync(
			socket,
			AvatarProtocolJson.CreateEnvelope(
				AvatarMessageType.Error,
				requestId,
				new ErrorPayload
				{
					Message = message
				}),
			cancellationToken);
	}

	private Task SendRegisterAsync(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		return SendEnvelopeAsync(
			socket,
			AvatarProtocolJson.CreateEnvelope(
				AvatarMessageType.Register,
				requestId: null,
				new RegisterPayload
				{
					AgentId = _options.AgentId,
					Hostname = _options.Hostname,
					Version = _options.Version
				}),
			cancellationToken);
	}

	private async Task CloseSocketAsync(ClientWebSocket socket)
	{
		try
		{
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Agent closing connection.", CancellationToken.None);
			}
			else if (socket.State is not WebSocketState.Closed and not WebSocketState.Aborted and not WebSocketState.None)
			{
				socket.Abort();
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "WebSocket close handshake failed.");
			socket.Abort();
		}
	}
}