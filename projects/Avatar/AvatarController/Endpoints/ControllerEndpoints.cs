using System.Text.Json;
using Avatar.Shared.Payloads;
using AvatarController.Configuration;
using AvatarController.Models;
using AvatarController.Services;
using Avatar.Shared.Protocol;

namespace AvatarController.Endpoints;

public static class ControllerEndpoints
{
	public static IEndpointRouteBuilder MapControllerEndpoints(this IEndpointRouteBuilder app)
	{
		// Capture a logger instance at startup to avoid creating one per request.
		var loggerFactory = app.ServiceProvider.GetRequiredService<ILoggerFactory>();
		var logger = loggerFactory.CreateLogger("AvatarController.Endpoints");

		app.MapGet("/health", (AgentManager agentManager, AvatarControllerOptions options, HttpContext context) =>
		{
			var response = new ControllerHealthResponse(
				Status: "ok",
				UtcNow: DateTimeOffset.UtcNow,
				ConnectedAgents: agentManager.GetConnectedSessions().Count,
				HeartbeatIntervalSeconds: options.HeartbeatIntervalSeconds,
				CommandTimeoutSeconds: options.CommandTimeoutSeconds);

			logger.LogDebug("HTTP GET /health called. ConnectedAgents={ConnectedAgents}", response.ConnectedAgents);

			LogRequestTrace(logger, context, response);

			return Results.Ok(response);
		});

		app.MapGet("/agents", (AgentManager agentManager, HttpContext context) =>
		{
			var sessions = agentManager.GetConnectedSessions().Select(AgentSummaryResponse.FromSession).ToList();
			logger.LogDebug("HTTP GET /agents called. ConnectedAgents={ConnectedAgents}", sessions.Count);

			LogRequestTrace(logger, context, sessions);

			return Results.Ok(sessions);
		});

		app.MapPost("/command", HandleCommandAsync);
		app.Map("/ws", static (HttpContext context, AgentConnectionHandler connectionHandler) => connectionHandler.HandleAsync(context));
		return app;
	}

	private static async Task<IResult> HandleCommandAsync(SendCommandApiRequest request, IAgentCommandService commandService, HttpContext context)
	{
		// Reuse the startup logger via the request services (same instance)
		var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
		var logger = loggerFactory.CreateLogger("AvatarController.Endpoints");

		logger.LogInformation("HTTP POST /command received. AgentId={AgentId}, Action={Action}, TraceId={TraceId}", request?.AgentId ?? "(null)", request?.Command?.Action ?? "(null)", context.TraceIdentifier);

		LogRequestTrace(logger, context, request);

		var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrWhiteSpace(request.AgentId))
		{
			errors["agentId"] = ["AgentId is required."];
		}

		if (request.Command is null)
		{
			errors["command"] = ["Command payload is required."];
		}
		else
		{
			foreach (var errorEntry in request.Command.Validate())
			{
				errors[errorEntry.Key] = errorEntry.Value;
			}
		}

		if (errors.Count > 0)
		{
			logger.LogWarning("Validation failed for POST /command: {Errors}", errors);
			return Results.ValidationProblem(errors);
		}

		var normalizedAgentId = request.AgentId!.Trim();
		logger.LogDebug("Dispatching command to agent {AgentId} (action: {Action}).", normalizedAgentId, request.Command!.Action);
		var dispatchResult = await commandService.SendCommandAsync(normalizedAgentId, request.Command!, context.RequestAborted);

		switch (dispatchResult.Status)
		{
			case AgentCommandDispatchStatus.Success:
				logger.LogInformation("Command to {AgentId} completed successfully (requestId: {RequestId}).", normalizedAgentId, dispatchResult.Completion?.RequestId ?? "none");
				return Results.Ok(SendCommandApiResponse.FromCompletion(normalizedAgentId, dispatchResult.Completion!));
			case AgentCommandDispatchStatus.NotFound:
				logger.LogWarning("Agent not found: {AgentId}", normalizedAgentId);
				return Results.NotFound(new { error = dispatchResult.Message });
			case AgentCommandDispatchStatus.TimedOut:
				logger.LogWarning("Command to {AgentId} timed out.", normalizedAgentId);
				return Results.Json(new { error = dispatchResult.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
			case AgentCommandDispatchStatus.AgentError:
				logger.LogWarning("Agent {AgentId} returned an error for request {RequestId}: {Message}", normalizedAgentId, dispatchResult.Completion?.RequestId ?? "none", dispatchResult.Message);
				return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Agent command failed.", detail: dispatchResult.Message);
			case AgentCommandDispatchStatus.AgentUnavailable:
				logger.LogWarning("Agent {AgentId} unavailable for command dispatch: {Message}", normalizedAgentId, dispatchResult.Message);
				return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Agent unavailable.", detail: dispatchResult.Message);
			default:
				logger.LogError("Unexpected command dispatch state for agent {AgentId}: {Status}", normalizedAgentId, dispatchResult.Status);
				return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected command dispatch state.");
		}
	}

	private static void LogRequestTrace(ILogger logger, HttpContext context, object? payload)
	{
		if (!logger.IsEnabled(LogLevel.Trace))
			return;

		var payloadText = TrySerializePayload(payload, logger);

		logger.LogTrace("HTTP details: TraceId={TraceId}, Timestamp={Timestamp}, Payload={Payload}", context.TraceIdentifier, DateTimeOffset.UtcNow, payloadText);
	}

	private static string? TrySerializePayload(object? payload, ILogger logger)
	{
		if (payload is null)
			return null;

		try
		{
			return JsonSerializer.Serialize(payload, AvatarProtocolJson.SerializerOptions);
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "Failed to serialize payload for trace logging.");
			return null;
		}
	}

}