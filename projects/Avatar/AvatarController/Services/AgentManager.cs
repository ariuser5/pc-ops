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
		return _sessionsByAgentId.Values.ToList().AsReadOnly();
	}

	public void Remove(AgentSession session)
	{
		if (_sessionsByAgentId.TryGetValue(session.AgentId, out var existing) && existing.SessionId == session.SessionId)
		{
			_sessionsByAgentId.TryRemove(session.AgentId, out _);
			_logger.LogInformation("Agent disconnected: {AgentId} ({Hostname}). Connected sessions: {Count}", session.AgentId, session.Hostname, _sessionsByAgentId.Count);
		}
	}

	public void Upsert(AgentSession session)
	{
		if (_sessionsByAgentId.TryGetValue(session.AgentId, out var existing) && existing.SessionId != session.SessionId)
		{
			_logger.LogWarning("Replacing existing session for agent {AgentId}. Old session: {OldSession}, new session: {NewSession}.", session.AgentId, existing.SessionId, session.SessionId);
		}

		_sessionsByAgentId[session.AgentId] = session;
		_logger.LogInformation("Agent connected: {AgentId} ({Hostname}) v{Version}. Connected sessions: {Count}", session.AgentId, session.Hostname, session.Version, _sessionsByAgentId.Count);
	}
}