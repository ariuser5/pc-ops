using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AvatarAgent.Models;
using AvatarAgent.Services;

namespace AvatarAgent.Endpoints;

public static class WebSocketEndpoints
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	public static IEndpointRouteBuilder MapWebSocketEndpoints(this IEndpointRouteBuilder app)
	{
		app.Map("/ws", HandleWebSocketAsync);
		return app;
	}

	private static async Task HandleWebSocketAsync(HttpContext context, ICommandExecutor commandExecutor, ILoggerFactory loggerFactory)
	{
		var logger = loggerFactory.CreateLogger("AvatarAgent.WebSocketEndpoints");
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
		logger.LogInformation("WebSocket connection opened from {RemoteIp}.", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

		while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
		{
			var incomingMessage = await ReceiveTextMessageAsync(socket, context.RequestAborted);
			if (incomingMessage is null)
			{
				break;
			}

			await ProcessCommandMessageAsync(incomingMessage, socket, commandExecutor, logger, context.RequestAborted);
		}

		if (socket.State == WebSocketState.Open)
		{
			await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed.", context.RequestAborted);
		}

		logger.LogInformation("WebSocket connection closed.");
	}

	private static async Task ProcessCommandMessageAsync(
		string incomingMessage,
		WebSocket socket,
		ICommandExecutor commandExecutor,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();

		CommandRequest? request;
		try
		{
			request = JsonSerializer.Deserialize<CommandRequest>(incomingMessage, JsonOptions);
		}
		catch (JsonException exception)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning(exception, "Invalid JSON payload rejected in {ElapsedMs} ms.", elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "error",
				action = "<invalid-json>",
				error = "Invalid JSON payload.",
				elapsedMs
			}, cancellationToken);
			return;
		}

		if (request is null)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning("Empty command payload rejected in {ElapsedMs} ms.", elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "error",
				action = "<missing>",
				error = "Request body is required.",
				elapsedMs
			}, cancellationToken);
			return;
		}

		var action = request.Action?.Trim() ?? "<missing>";
		logger.LogInformation("Incoming websocket command {Action}: {Payload}", action, incomingMessage);

		var validationErrors = request.Validate();
		if (validationErrors.Count > 0)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning("Websocket command {Action} failed validation in {ElapsedMs} ms.", action, elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "error",
				action,
				error = "Invalid command request.",
				errors = validationErrors,
				elapsedMs
			}, cancellationToken);
			return;
		}

		try
		{
			var result = await commandExecutor.ExecuteAsync(request, cancellationToken);
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogInformation("Websocket command {Action} succeeded in {ElapsedMs} ms.", result.Action, elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "ok",
				action = result.Action,
				message = result.Message,
				elapsedMs
			}, cancellationToken);
		}
		catch (ArgumentException exception)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning(exception, "Websocket command {Action} failed in {ElapsedMs} ms.", action, elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "error",
				action,
				error = exception.Message,
				elapsedMs
			}, cancellationToken);
		}
		catch (Exception exception)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogError(exception, "Websocket command {Action} failed in {ElapsedMs} ms.", action, elapsedMs);
			await SendJsonAsync(socket, new
			{
				status = "error",
				action,
				error = "Command execution failed.",
				elapsedMs
			}, cancellationToken);
		}
	}

	private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[4096];
		using var payload = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			if (result.MessageType != WebSocketMessageType.Text)
			{
				continue;
			}

			payload.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				break;
			}
		}

		return Encoding.UTF8.GetString(payload.ToArray());
	}

	private static Task SendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
	{
		if (socket.State != WebSocketState.Open)
		{
			return Task.CompletedTask;
		}

		var json = JsonSerializer.Serialize(payload, JsonOptions);
		var bytes = Encoding.UTF8.GetBytes(json);
		return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
	}
}