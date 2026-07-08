using System.Collections.Concurrent;
using System.Net.WebSockets;
using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;

namespace AvatarController.Services;

public sealed class AgentSession
{
	private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentCommandCompletion>> _pendingCommands = new(StringComparer.OrdinalIgnoreCase);
	private long _lastHeartbeatUnixTimeMilliseconds;
	private readonly ILogger<AgentSession> _logger;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly WebSocket _socket;
	private int _isClosed;

	public AgentSession(string agentId, string hostname, string version, WebSocket socket, ILogger<AgentSession> logger)
	{
		AgentId = agentId;
		Hostname = hostname;
		Version = version;
		_socket = socket;
		_logger = logger;
		SessionId = Guid.NewGuid().ToString("N");
		ConnectedAt = DateTimeOffset.UtcNow;
		_lastHeartbeatUnixTimeMilliseconds = ConnectedAt.ToUnixTimeMilliseconds();
	}

	public string AgentId { get; }

	public DateTimeOffset ConnectedAt { get; }

	public string Hostname { get; }

	public bool IsOpen => _socket.State == WebSocketState.Open;

	public DateTimeOffset LastHeartbeatAt => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastHeartbeatUnixTimeMilliseconds));

	public string SessionId { get; }

	public string Version { get; }

	public async Task CloseAsync(string reason, CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _isClosed, 1) == 1)
		{
			return;
		}

		FailPendingCommands(reason);

		try
		{
			if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
			}
			else if (_socket.State is not WebSocketState.Closed and not WebSocketState.Aborted and not WebSocketState.None)
			{
				_socket.Abort();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Error while closing websocket for agent {AgentId}.", AgentId);
		}
	}

	public bool IsHeartbeatExpired(TimeSpan timeout, DateTimeOffset utcNow)
	{
		return utcNow - LastHeartbeatAt > timeout;
	}

	public void MarkHeartbeatReceived(string? requestId)
	{
		Interlocked.Exchange(ref _lastHeartbeatUnixTimeMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		_logger.LogDebug("Heartbeat acknowledged by agent {AgentId} (requestId: {RequestId}).", AgentId, requestId ?? "none");
	}

	public Task<string?> ReceiveAsync(CancellationToken cancellationToken)
	{
		return WebSocketTextMessageCodec.ReceiveTextAsync(_socket, cancellationToken);
	}

	public async Task<AgentCommandCompletion> SendCommandAsync(CommandRequest command, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid().ToString("N");
		var pendingCommand = new TaskCompletionSource<AgentCommandCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);

		if (!_pendingCommands.TryAdd(requestId, pendingCommand))
		{
			throw new InvalidOperationException($"A pending request with id '{requestId}' already exists.");
		}

		try
		{
			await SendAsync(AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Command, requestId, command), cancellationToken);
			_logger.LogDebug("Awaiting command result from agent {AgentId} for request {RequestId} with timeout {TimeoutSeconds} second(s).", AgentId, requestId, timeout.TotalSeconds);
			return await pendingCommand.Task.WaitAsync(timeout, cancellationToken);
		}
		finally
		{
			_pendingCommands.TryRemove(requestId, out _);
		}
	}

	public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid().ToString("N");
		await SendAsync(AvatarProtocolJson.CreateEnvelope<object?>(AvatarMessageType.Heartbeat, requestId, null), cancellationToken);
	}

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
			if (envelope.TryGetMessageType(out var messageType) && messageType == AvatarMessageType.Heartbeat)
			{
				_logger.LogDebug("Sent {MessageType} to agent {AgentId} (requestId: {RequestId}).", envelope.Type, AgentId, envelope.RequestId ?? "none");
			}
			else
			{
				_logger.LogInformation("Sent {MessageType} to agent {AgentId} (requestId: {RequestId}).", envelope.Type, AgentId, envelope.RequestId ?? "none");
			}
			_logger.LogDebug("Sent raw message to agent {AgentId}: {Payload}", AgentId, json);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	public bool TryCompletePendingCommandError(string? requestId, ErrorPayload error)
	{
		if (string.IsNullOrWhiteSpace(requestId) || !_pendingCommands.TryRemove(requestId, out var pendingCommand))
		{
			return false;
		}

		pendingCommand.TrySetResult(new AgentCommandCompletion(requestId, null, error));
		return true;
	}

	public bool TryCompletePendingCommandResult(string? requestId, CommandResultPayload result)
	{
		if (string.IsNullOrWhiteSpace(requestId) || !_pendingCommands.TryRemove(requestId, out var pendingCommand))
		{
			return false;
		}

		pendingCommand.TrySetResult(new AgentCommandCompletion(requestId, result, null));
		return true;
	}

	private void FailPendingCommands(string message)
	{
		foreach (var pendingPair in _pendingCommands.ToArray())
		{
			if (_pendingCommands.TryRemove(pendingPair.Key, out var pendingCommand))
			{
				pendingCommand.TrySetResult(new AgentCommandCompletion(pendingPair.Key, null, new ErrorPayload
				{
					Message = message
				}));
			}
		}
	}
}