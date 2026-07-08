using System.Net.WebSockets;
using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;

namespace AvatarController.Services;

public sealed class AgentConnectionHandler
{
	private readonly AgentManager _agentManager;
	private readonly ILogger<AgentConnectionHandler> _logger;
	private readonly ILoggerFactory _loggerFactory;

	public AgentConnectionHandler(AgentManager agentManager, ILogger<AgentConnectionHandler> logger, ILoggerFactory loggerFactory)
	{
		_agentManager = agentManager;
		_logger = logger;
		_loggerFactory = loggerFactory;
	}

	public async Task HandleAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			await context.Response.WriteAsJsonAsync(new
			{
				status = "error",
				error = "WebSocket upgrade required. Connect to /ws using ws:// or wss://."
			}, context.RequestAborted);
			return;
		}

		using var socket = await context.WebSockets.AcceptWebSocketAsync();
		_logger.LogInformation("Incoming websocket connection from {RemoteIp}.", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

		AgentSession? session = null;
		try
		{
			session = await RegisterAgentAsync(socket, context.RequestAborted);
			if (session is null)
			{
				return;
			}

			await _agentManager.UpsertAsync(session, context.RequestAborted);
			await ProcessMessagesAsync(session, context.RequestAborted);
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			_logger.LogDebug("WebSocket handling cancelled for remote endpoint {RemoteIp}.", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
		}
		finally
		{
			if (session is not null)
			{
				await _agentManager.RemoveAsync(session, "WebSocket connection closed.", CancellationToken.None);
			}
			else if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed.", CancellationToken.None);
			}

			_logger.LogInformation("WebSocket connection closed.");
		}
	}

	private async Task HandleAgentMessageAsync(AgentSession session, string incoming, CancellationToken cancellationToken)
	{
		if (!AvatarProtocolJson.TryDeserializeEnvelope(incoming, out var envelope, out var parseError) || envelope is null)
		{
			_logger.LogWarning("Agent {AgentId} sent invalid envelope: {Error}", session.AgentId, parseError);
			await SendErrorAsync(session, null, parseError, cancellationToken);
			return;
		}

		if (!envelope.TryGetMessageType(out var messageType))
		{
			var error = $"Unsupported message type '{envelope.Type}'.";
			_logger.LogWarning("Agent {AgentId} sent unsupported message type: {Type}", session.AgentId, envelope.Type);
			await SendErrorAsync(session, envelope.RequestId, error, cancellationToken);
			return;
		}

		switch (messageType)
		{
			case AvatarMessageType.Result:
				if (!AvatarProtocolJson.TryDeserializePayload<CommandResultPayload>(envelope, out var resultPayload, out var resultError) || resultPayload is null)
				{
					await SendErrorAsync(session, envelope.RequestId, resultError, cancellationToken);
					return;
				}

				if (!session.TryCompletePendingCommandResult(envelope.RequestId, resultPayload))
				{
					_logger.LogDebug("Received unmatched result from agent {AgentId} for request {RequestId}.", session.AgentId, envelope.RequestId ?? "none");
				}

				if (resultPayload.TryGetStatus(out var resultStatus, out _))
				{
					_logger.LogInformation(
						"Agent {AgentId} completed request {RequestId} with status {Status} in {ElapsedMs} ms.",
						session.AgentId,
						envelope.RequestId ?? "none",
						resultStatus.ToProtocolValue(),
						resultPayload.ElapsedMs);
				}
				else
				{
					_logger.LogWarning("Agent {AgentId} returned result with unsupported status '{Status}'.", session.AgentId, resultPayload.Status);
				}
				break;

			case AvatarMessageType.Error:
				if (!AvatarProtocolJson.TryDeserializePayload<ErrorPayload>(envelope, out var errorPayload, out var payloadError) || errorPayload is null)
				{
					await SendErrorAsync(session, envelope.RequestId, payloadError, cancellationToken);
					return;
				}

				if (!session.TryCompletePendingCommandError(envelope.RequestId, errorPayload))
				{
					_logger.LogWarning("Agent {AgentId} returned an uncorrelated error for request {RequestId}: {Message}", session.AgentId, envelope.RequestId ?? "none", errorPayload.Message);
				}
				else
				{
					_logger.LogWarning("Agent {AgentId} returned error for request {RequestId}: {Message}", session.AgentId, envelope.RequestId ?? "none", errorPayload.Message);
				}
				break;

			case AvatarMessageType.Heartbeat:
				session.MarkHeartbeatReceived(envelope.RequestId);
				_logger.LogDebug("Heartbeat received from {AgentId} (requestId: {RequestId}).", session.AgentId, envelope.RequestId ?? "none");
				break;

			default:
				_logger.LogDebug("Ignoring message type {MessageType} from {AgentId}.", messageType, session.AgentId);
				break;
		}
	}

	private async Task ProcessMessagesAsync(AgentSession session, CancellationToken cancellationToken)
	{
		while (session.IsOpen && !cancellationToken.IsCancellationRequested)
		{
			var incoming = await session.ReceiveAsync(cancellationToken);
			if (incoming is null)
			{
				_logger.LogWarning("Agent {AgentId} closed the websocket connection.", session.AgentId);
				break;
			}

			_logger.LogDebug("Received raw message from agent {AgentId}: {Payload}", session.AgentId, incoming);
			await HandleAgentMessageAsync(session, incoming, cancellationToken);
		}
	}

	private async Task<AgentSession?> RegisterAgentAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var registerLogger = _loggerFactory.CreateLogger("AvatarController.Register");
		var incoming = await WebSocketTextMessageCodec.ReceiveTextAsync(socket, cancellationToken);
		if (incoming is null)
		{
			registerLogger.LogWarning("Connection closed before register message was received.");
			return null;
		}

		registerLogger.LogDebug("Received raw register message: {Payload}", incoming);

		if (!AvatarProtocolJson.TryDeserializeEnvelope(incoming, out var envelope, out var parseError) || envelope is null)
		{
			registerLogger.LogWarning("Invalid register envelope: {Error}", parseError);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, null, new ErrorPayload { Message = parseError }), cancellationToken);
			return null;
		}

		if (!envelope.TryGetMessageType(out var messageType) || messageType != AvatarMessageType.Register)
		{
			const string error = "First message must be type 'register'.";
			registerLogger.LogWarning(error);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = error }), cancellationToken);
			return null;
		}

		if (!AvatarProtocolJson.TryDeserializePayload<RegisterPayload>(envelope, out var registerPayload, out var payloadError) || registerPayload is null)
		{
			registerLogger.LogWarning("Invalid register payload: {Error}", payloadError);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = payloadError }), cancellationToken);
			return null;
		}

		if (string.IsNullOrWhiteSpace(registerPayload.AgentId) || string.IsNullOrWhiteSpace(registerPayload.Hostname) || string.IsNullOrWhiteSpace(registerPayload.Version))
		{
			const string error = "Register payload requires agentId, hostname, and version.";
			registerLogger.LogWarning(error);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = error }), cancellationToken);
			return null;
		}

		registerLogger.LogInformation("Register received for agent {AgentId} ({Hostname}) v{Version}.", registerPayload.AgentId, registerPayload.Hostname, registerPayload.Version);
		var sessionLogger = _loggerFactory.CreateLogger<AgentSession>();
		return new AgentSession(registerPayload.AgentId, registerPayload.Hostname, registerPayload.Version, socket, sessionLogger);
	}

	private Task SendEnvelopeAsync(WebSocket socket, AvatarEnvelope envelope, CancellationToken cancellationToken)
	{
		if (socket.State != WebSocketState.Open)
		{
			return Task.CompletedTask;
		}

		return WebSocketTextMessageCodec.SendTextAsync(socket, AvatarProtocolJson.Serialize(envelope), cancellationToken);
	}

	private Task SendErrorAsync(AgentSession session, string? requestId, string message, CancellationToken cancellationToken)
	{
		return session.SendAsync(
			AvatarProtocolJson.CreateEnvelope(
				AvatarMessageType.Error,
				requestId,
				new ErrorPayload
				{
					Message = message
				}),
			cancellationToken);
	}
}