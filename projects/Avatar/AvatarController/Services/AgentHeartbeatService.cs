using AvatarController.Configuration;

namespace AvatarController.Services;

public sealed class AgentHeartbeatService : BackgroundService
{
	private readonly AgentManager _agentManager;
	private readonly ILogger<AgentHeartbeatService> _logger;
	private readonly AvatarControllerOptions _options;

	public AgentHeartbeatService(AgentManager agentManager, AvatarControllerOptions options, ILogger<AgentHeartbeatService> logger)
	{
		_agentManager = agentManager;
		_options = options;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_options.HeartbeatInterval);

		while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
		{
			var utcNow = DateTimeOffset.UtcNow;

			foreach (var session in _agentManager.GetConnectedSessions())
			{
				if (session.IsHeartbeatExpired(_options.HeartbeatTimeout, utcNow))
				{
					_logger.LogWarning("Heartbeat timed out for agent {AgentId}. Last heartbeat at {LastHeartbeat}. Removing session.", session.AgentId, session.LastHeartbeatAt);
					await _agentManager.RemoveAsync(session, "Heartbeat timed out.", stoppingToken);
					continue;
				}

				try
				{
					_logger.LogDebug("Sending heartbeat to agent {AgentId}. Last heartbeat at {LastHeartbeat}.", session.AgentId, session.LastHeartbeatAt);
					await session.SendHeartbeatAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Heartbeat send failed for agent {AgentId}. Removing session.", session.AgentId);
					await _agentManager.RemoveAsync(session, "Heartbeat send failed.", CancellationToken.None);
				}
			}
		}
	}
}