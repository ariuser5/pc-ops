using System.Diagnostics;
using AvatarAgent.Models;
using AvatarAgent.Services;

namespace AvatarAgent.Endpoints;

public static class CommandEndpoints
{
	public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet("/health", static () => Results.Ok(new HealthResponse("ok")));
		app.MapPost("/command", ExecuteCommandAsync);
		return app;
	}

	private static async Task<IResult> ExecuteCommandAsync(
		CommandRequest request,
		ICommandExecutor commandExecutor,
		ILoggerFactory loggerFactory,
		CancellationToken cancellationToken)
	{
		var logger = loggerFactory.CreateLogger("AvatarAgent.CommandEndpoints");
		var stopwatch = Stopwatch.StartNew();
		var action = request.Action?.Trim() ?? "<missing>";

		logger.LogInformation("Incoming command request {Action}: {@Request}", action, request);

		var validationErrors = request.Validate();
		if (validationErrors.Count > 0)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning("Command request {Action} failed validation in {ElapsedMs} ms.", action, elapsedMs);
			return Results.ValidationProblem(validationErrors, statusCode: StatusCodes.Status400BadRequest, title: "Invalid command request");
		}

		try
		{
			var result = await commandExecutor.ExecuteAsync(request, cancellationToken);
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogInformation("Command request {Action} succeeded in {ElapsedMs} ms.", result.Action, elapsedMs);
			return Results.Ok(new CommandResponse("ok", result.Action, result.Message, elapsedMs));
		}
		catch (ArgumentException exception)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogWarning(exception, "Command request {Action} failed in {ElapsedMs} ms.", action, elapsedMs);
			return Results.BadRequest(new ErrorResponse("error", action, exception.Message, elapsedMs));
		}
		catch (Exception exception)
		{
			stopwatch.Stop();
			var elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
			logger.LogError(exception, "Command request {Action} failed in {ElapsedMs} ms.", action, elapsedMs);
			return Results.Json(new ErrorResponse("error", action, "Command execution failed.", elapsedMs), statusCode: StatusCodes.Status500InternalServerError);
		}
	}

	private sealed record CommandResponse(string Status, string Action, string Message, double ElapsedMs);

	private sealed record ErrorResponse(string Status, string Action, string Error, double ElapsedMs);

	private sealed record HealthResponse(string Status);
}