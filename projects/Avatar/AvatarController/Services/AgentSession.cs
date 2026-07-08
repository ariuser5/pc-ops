using System.Net.WebSockets;
using Avatar.Shared.Protocol;

namespace AvatarController.Services;

public sealed class AgentSession
{
	private readonly ILogger<AgentSession> _logger;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly WebSocket _socket;

	public AgentSession(string agentId, string hostname, string version, WebSocket socket, ILogger<AgentSession> logger)
	{
		AgentId = agentId;
		Hostname = hostname;
		Version = version;
		_socket = socket;
		_logger = logger;
		SessionId = Guid.NewGuid().ToString("N");
		ConnectedAt = DateTimeOffset.UtcNow;
	}

	public string AgentId { get; }

	public DateTimeOffset ConnectedAt { get; }

	public string Hostname { get; }

	public string SessionId { get; }

	public string Version { get; }

	public async Task SendAsync(AvatarEnvelope envelope, CancellationToken cancellationToken)
	{
		if (_socket.State != WebSocketState.Open)
		{
			throw new InvalidOperationException("Cannot send to a closed websocket connection.");
		}

		var json = AvatarProtocolJson.Serialize(envelope);
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await WebSocketTextMessageCodec.SendTextAsync(_socket, json, cancellationToken);
			_logger.LogInformation("Sent {MessageType} to agent {AgentId} (requestId: {RequestId}).", envelope.Type, AgentId, envelope.RequestId ?? "none");
		}
		finally
		{
			_sendLock.Release();
		}
	}
}