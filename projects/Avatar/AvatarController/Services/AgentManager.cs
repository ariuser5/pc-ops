using System.Collections.Concurrent;

namespace AvatarController.Services;

public sealed class AgentManager
{
	private readonly ILogger<AgentManager> _logger;
	private readonly ConcurrentDictionary<string, AgentSession> _sessionsByAgentId = new(StringComparer.OrdinalIgnoreCase);

	public AgentManager(ILogger<AgentManager> logger)
	{
		_logger = logger;
	}

	public IReadOnlyCollection<AgentSession> GetConnectedSessions()
	{
		return _sessionsByAgentId.Values
			.OrderBy(static session => session.AgentId, StringComparer.OrdinalIgnoreCase)
			.ToList()
			.AsReadOnly();
	}

	public async Task RemoveAsync(AgentSession session, string reason, CancellationToken cancellationToken)
	{
		if (_sessionsByAgentId.TryGetValue(session.AgentId, out var existing) && existing.SessionId == session.SessionId)
		{
			_sessionsByAgentId.TryRemove(session.AgentId, out _);
			_logger.LogInformation("Agent disconnected: {AgentId} ({Hostname}). Reason: {Reason}. Connected sessions: {Count}", session.AgentId, session.Hostname, reason, _sessionsByAgentId.Count);
		}

		await session.CloseAsync(reason, cancellationToken);
	}

	public bool TryGetSession(string agentId, out AgentSession? session)
	{
		return _sessionsByAgentId.TryGetValue(agentId, out session);
	}

	public async Task UpsertAsync(AgentSession session, CancellationToken cancellationToken)
	{
		AgentSession? replacedSession = null;
		if (_sessionsByAgentId.TryGetValue(session.AgentId, out var existing) && existing.SessionId != session.SessionId)
		{
			replacedSession = existing;
			_logger.LogWarning("Replacing existing session for agent {AgentId}. Old session: {OldSession}, new session: {NewSession}.", session.AgentId, existing.SessionId, session.SessionId);
		}

		_sessionsByAgentId[session.AgentId] = session;

		if (replacedSession is not null)
		{
			await replacedSession.CloseAsync("Replaced by a newer session.", cancellationToken);
		}

		_logger.LogInformation("Agent connected: {AgentId} ({Hostname}) v{Version}. Connected sessions: {Count}", session.AgentId, session.Hostname, session.Version, _sessionsByAgentId.Count);
	}
}