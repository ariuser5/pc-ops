using Avatar.Shared.Payloads;
using AvatarController.Configuration;

namespace AvatarController.Services;

public interface IAgentCommandService
{
	Task<AgentCommandDispatchResult> SendCommandAsync(string agentId, CommandRequest command, CancellationToken cancellationToken);
}

public sealed class AgentCommandService : IAgentCommandService
{
	private readonly AgentManager _agentManager;
	private readonly ILogger<AgentCommandService> _logger;
	private readonly AvatarControllerOptions _options;

	public AgentCommandService(AgentManager agentManager, AvatarControllerOptions options, ILogger<AgentCommandService> logger)
	{
		_agentManager = agentManager;
		_options = options;
		_logger = logger;
	}

	public async Task<AgentCommandDispatchResult> SendCommandAsync(string agentId, CommandRequest command, CancellationToken cancellationToken)
	{
		if (!_agentManager.TryGetSession(agentId, out var session) || session is null)
		{
			return AgentCommandDispatchResult.AgentNotFound(agentId);
		}

		try
		{
			var completion = await session.SendCommandAsync(command, _options.CommandTimeout, cancellationToken);
			return completion.Error is null
				? AgentCommandDispatchResult.Success(agentId, completion)
				: AgentCommandDispatchResult.AgentError(agentId, completion);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning("Timed out waiting for agent {AgentId} to answer a command after {TimeoutSeconds} second(s).", agentId, _options.CommandTimeoutSeconds);
			return AgentCommandDispatchResult.TimedOut(agentId);
		}
		catch (InvalidOperationException exception)
		{
			_logger.LogWarning(exception, "Agent {AgentId} is unavailable for command dispatch.", agentId);
			return AgentCommandDispatchResult.AgentUnavailable(agentId, exception.Message);
		}
	}
}

public sealed record AgentCommandCompletion(string RequestId, CommandResultPayload? Result, ErrorPayload? Error);

public enum AgentCommandDispatchStatus
{
	Success,
	NotFound,
	TimedOut,
	AgentError,
	AgentUnavailable
}

public sealed record AgentCommandDispatchResult(
	AgentCommandDispatchStatus Status,
	string AgentId,
	AgentCommandCompletion? Completion,
	string? Message)
{
	public static AgentCommandDispatchResult AgentError(string agentId, AgentCommandCompletion completion)
	{
		return new AgentCommandDispatchResult(AgentCommandDispatchStatus.AgentError, agentId, completion, completion.Error?.Message);
	}

	public static AgentCommandDispatchResult AgentNotFound(string agentId)
	{
		return new AgentCommandDispatchResult(AgentCommandDispatchStatus.NotFound, agentId, null, $"Agent '{agentId}' is not connected.");
	}

	public static AgentCommandDispatchResult AgentUnavailable(string agentId, string message)
	{
		return new AgentCommandDispatchResult(AgentCommandDispatchStatus.AgentUnavailable, agentId, null, message);
	}

	public static AgentCommandDispatchResult Success(string agentId, AgentCommandCompletion completion)
	{
		return new AgentCommandDispatchResult(AgentCommandDispatchStatus.Success, agentId, completion, null);
	}

	public static AgentCommandDispatchResult TimedOut(string agentId)
	{
		return new AgentCommandDispatchResult(AgentCommandDispatchStatus.TimedOut, agentId, null, $"Agent '{agentId}' did not answer before the configured timeout.");
	}
}