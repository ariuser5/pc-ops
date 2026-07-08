using System.Net.WebSockets;
using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;
using AvatarController.Services;

namespace AvatarController.Endpoints;

public static class ControllerEndpoints
{
	public static IEndpointRouteBuilder MapControllerEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet("/health", static () => Results.Ok(new { status = "ok" }));
		app.Map("/ws", HandleWebSocketAsync);
		return app;
	}

	private static async Task HandleWebSocketAsync(HttpContext context, AgentManager agentManager, ILoggerFactory loggerFactory)
	{
		var logger = loggerFactory.CreateLogger("AvatarController.Connection");
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
		logger.LogInformation("Incoming websocket connection from {RemoteIp}.", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

		AgentSession? session = null;
		try
		{
			session = await RegisterAgentAsync(socket, loggerFactory, context.RequestAborted);
			if (session is null)
			{
				return;
			}

			agentManager.Upsert(session);

			await SendTestCommandAsync(session, context.RequestAborted);

			while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
			{
				var incoming = await WebSocketTextMessageCodec.ReceiveTextAsync(socket, context.RequestAborted);
				if (incoming is null)
				{
					break;
				}

				logger.LogInformation("Received from agent {AgentId}: {Payload}", session.AgentId, incoming);
				await HandleAgentMessageAsync(session, incoming, logger, context.RequestAborted);
			}
		}
		finally
		{
			if (session is not null)
			{
				agentManager.Remove(session);
			}

			if (socket.State == WebSocketState.Open)
			{
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed.", context.RequestAborted);
			}

			logger.LogInformation("WebSocket connection closed.");
		}
	}

	private static async Task HandleAgentMessageAsync(AgentSession session, string incoming, ILogger logger, CancellationToken cancellationToken)
	{
		if (!AvatarProtocolJson.TryDeserializeEnvelope(incoming, out var envelope, out var parseError) || envelope is null)
		{
			logger.LogWarning("Agent {AgentId} sent invalid envelope: {Error}", session.AgentId, parseError);
			await SendErrorAsync(session, null, parseError, cancellationToken);
			return;
		}

		if (!AvatarMessageTypeExtensions.TryParseProtocolValue(envelope.Type, out var messageType))
		{
			var error = $"Unsupported message type '{envelope.Type}'.";
			logger.LogWarning("Agent {AgentId} sent unsupported message type: {Type}", session.AgentId, envelope.Type);
			await SendErrorAsync(session, envelope.RequestId, error, cancellationToken);
			return;
		}

		switch (messageType)
		{
			case AvatarMessageType.Result:
				if (AvatarProtocolJson.TryDeserializePayload<CommandResultPayload>(envelope, out var resultPayload, out var resultError) && resultPayload is not null)
				{
					logger.LogInformation(
						"Agent {AgentId} completed request {RequestId} with status {Status} in {ElapsedMs} ms.",
						session.AgentId,
						envelope.RequestId ?? "none",
						resultPayload.Status,
						resultPayload.ElapsedMs);
				}
				else
				{
					await SendErrorAsync(session, envelope.RequestId, resultError, cancellationToken);
				}
				break;

			case AvatarMessageType.Error:
				if (AvatarProtocolJson.TryDeserializePayload<ErrorPayload>(envelope, out var errorPayload, out _) && errorPayload is not null)
				{
					logger.LogWarning("Agent {AgentId} returned error for request {RequestId}: {Message}", session.AgentId, envelope.RequestId ?? "none", errorPayload.Message);
				}
				break;

			case AvatarMessageType.Heartbeat:
				logger.LogDebug("Heartbeat received from {AgentId}.", session.AgentId);
				break;

			default:
				logger.LogDebug("Ignoring message type {MessageType} from {AgentId}.", messageType, session.AgentId);
				break;
		}
	}

	private static async Task<AgentSession?> RegisterAgentAsync(WebSocket socket, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
	{
		var logger = loggerFactory.CreateLogger("AvatarController.Register");
		var incoming = await WebSocketTextMessageCodec.ReceiveTextAsync(socket, cancellationToken);
		if (incoming is null)
		{
			logger.LogWarning("Connection closed before register message was received.");
			return null;
		}

		if (!AvatarProtocolJson.TryDeserializeEnvelope(incoming, out var envelope, out var parseError) || envelope is null)
		{
			logger.LogWarning("Invalid register envelope: {Error}", parseError);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, null, new ErrorPayload { Message = parseError }), cancellationToken);
			return null;
		}

		if (!AvatarMessageTypeExtensions.TryParseProtocolValue(envelope.Type, out var messageType) || messageType != AvatarMessageType.Register)
		{
			const string error = "First message must be type 'register'.";
			logger.LogWarning(error);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = error }), cancellationToken);
			return null;
		}

		if (!AvatarProtocolJson.TryDeserializePayload<RegisterPayload>(envelope, out var registerPayload, out var payloadError) || registerPayload is null)
		{
			logger.LogWarning("Invalid register payload: {Error}", payloadError);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = payloadError }), cancellationToken);
			return null;
		}

		if (string.IsNullOrWhiteSpace(registerPayload.AgentId) || string.IsNullOrWhiteSpace(registerPayload.Hostname) || string.IsNullOrWhiteSpace(registerPayload.Version))
		{
			const string error = "Register payload requires agentId, hostname, and version.";
			logger.LogWarning(error);
			await SendEnvelopeAsync(socket, AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Error, envelope.RequestId, new ErrorPayload { Message = error }), cancellationToken);
			return null;
		}

		logger.LogInformation("Register received for agent {AgentId} ({Hostname}) v{Version}.", registerPayload.AgentId, registerPayload.Hostname, registerPayload.Version);
		var sessionLogger = loggerFactory.CreateLogger<AgentSession>();
		return new AgentSession(registerPayload.AgentId, registerPayload.Hostname, registerPayload.Version, socket, sessionLogger);
	}

	private static Task SendEnvelopeAsync(WebSocket socket, AvatarEnvelope envelope, CancellationToken cancellationToken)
	{
		if (socket.State != WebSocketState.Open)
		{
			return Task.CompletedTask;
		}

		return WebSocketTextMessageCodec.SendTextAsync(socket, AvatarProtocolJson.Serialize(envelope), cancellationToken);
	}

	private static Task SendErrorAsync(AgentSession session, string? requestId, string message, CancellationToken cancellationToken)
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

	private static Task SendTestCommandAsync(AgentSession session, CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid().ToString("N");
		var command = new CommandRequest
		{
			Action = "MoveMouse",
			X = 500,
			Y = 300
		};

		return session.SendAsync(AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Command, requestId, command), cancellationToken);
	}
}