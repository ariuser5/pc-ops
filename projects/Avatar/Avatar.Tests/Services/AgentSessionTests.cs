using System.Net.WebSockets;
using System.Text;
using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;
using AvatarController.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avatar.Tests.Services;

public sealed class AgentSessionTests
{
	private static AgentSession CreateSession(WebSocket socket, string agentId = "agent-1")
	{
		return new AgentSession(agentId, "test-host", "1.0.0", socket, NullLogger<AgentSession>.Instance);
	}

	private static WebSocket CreateOpenSocketMock()
	{
		var socket = Substitute.For<WebSocket>();
		socket.State.Returns(WebSocketState.Open);
		socket.SendAsync(
			Arg.Any<ArraySegment<byte>>(),
			Arg.Any<WebSocketMessageType>(),
			Arg.Any<bool>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);
		return socket;
	}

	// ── Heartbeat expiry ─────────────────────────────────────────────────────

	[Fact]
	public void IsHeartbeatExpired_WithinTimeout_ReturnsFalse()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var utcNow = session.LastHeartbeatAt.AddSeconds(29);

		Assert.False(session.IsHeartbeatExpired(TimeSpan.FromSeconds(30), utcNow));
	}

	[Fact]
	public void IsHeartbeatExpired_ExactlyAtTimeout_ReturnsFalse()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var utcNow = session.LastHeartbeatAt.AddSeconds(30);

		Assert.False(session.IsHeartbeatExpired(TimeSpan.FromSeconds(30), utcNow));
	}

	[Fact]
	public void IsHeartbeatExpired_AfterTimeout_ReturnsTrue()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var utcNow = session.LastHeartbeatAt.AddSeconds(91);

		Assert.True(session.IsHeartbeatExpired(TimeSpan.FromSeconds(90), utcNow));
	}

	[Fact]
	public void MarkHeartbeatReceived_UpdatesLastHeartbeatAt()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var before = session.LastHeartbeatAt;

		// Advance time and mark received
		Thread.Sleep(10);
		session.MarkHeartbeatReceived("req-1");

		Assert.True(session.LastHeartbeatAt >= before);
	}

	// ── Pending command completion ────────────────────────────────────────────

	[Fact]
	public async Task TryCompletePendingCommandResult_MatchingRequest_CompletesTask()
	{
		var sentPayload = new TaskCompletionSource<string>();
		var socket = CreateOpenSocketMock();
		socket.SendAsync(
			Arg.Any<ArraySegment<byte>>(),
			Arg.Any<WebSocketMessageType>(),
			Arg.Any<bool>(),
			Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				var segment = ci.ArgAt<ArraySegment<byte>>(0);
				var json = Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);
				sentPayload.TrySetResult(json);
				return Task.CompletedTask;
			});

		var session = CreateSession(socket);
		var command = new CommandRequest { Action = "MoveMouse", X = 10, Y = 20 };

		var sendTask = session.SendCommandAsync(command, TimeSpan.FromSeconds(5), CancellationToken.None);

		// Extract the requestId from the sent envelope
		var json = await sentPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));
		AvatarProtocolJson.TryDeserializeEnvelope(json, out var envelope, out _);
		var requestId = envelope!.RequestId;

		var result = new CommandResultPayload { Status = "success", ElapsedMs = 42 };
		var completed = session.TryCompletePendingCommandResult(requestId, result);

		Assert.True(completed);
		var completion = await sendTask;
		Assert.NotNull(completion.Result);
		Assert.Equal("success", completion.Result.Status);
		Assert.Equal(42, completion.Result.ElapsedMs);
		Assert.Null(completion.Error);
	}

	[Fact]
	public async Task TryCompletePendingCommandError_MatchingRequest_CompletesTaskWithError()
	{
		var sentPayload = new TaskCompletionSource<string>();
		var socket = CreateOpenSocketMock();
		socket.SendAsync(
			Arg.Any<ArraySegment<byte>>(),
			Arg.Any<WebSocketMessageType>(),
			Arg.Any<bool>(),
			Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				var segment = ci.ArgAt<ArraySegment<byte>>(0);
				sentPayload.TrySetResult(Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count));
				return Task.CompletedTask;
			});

		var session = CreateSession(socket);
		var command = new CommandRequest { Action = "MoveMouse", X = 1, Y = 2 };

		var sendTask = session.SendCommandAsync(command, TimeSpan.FromSeconds(5), CancellationToken.None);

		var json = await sentPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));
		AvatarProtocolJson.TryDeserializeEnvelope(json, out var envelope, out _);
		var requestId = envelope!.RequestId;

		var error = new ErrorPayload { Message = "something went wrong" };
		var completed = session.TryCompletePendingCommandError(requestId, error);

		Assert.True(completed);
		var completion = await sendTask;
		Assert.NotNull(completion.Error);
		Assert.Equal("something went wrong", completion.Error.Message);
		Assert.Null(completion.Result);
	}

	[Fact]
	public void TryCompletePendingCommandResult_UnknownRequestId_ReturnsFalse()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var result = new CommandResultPayload { Status = "success", ElapsedMs = 1 };

		var completed = session.TryCompletePendingCommandResult("nonexistent-id", result);

		Assert.False(completed);
	}

	[Fact]
	public void TryCompletePendingCommandError_NullRequestId_ReturnsFalse()
	{
		var session = CreateSession(CreateOpenSocketMock());
		var error = new ErrorPayload { Message = "oops" };

		var completed = session.TryCompletePendingCommandError(null, error);

		Assert.False(completed);
	}

	// ── Pending commands cancelled on close ───────────────────────────────────

	[Fact]
	public async Task CloseAsync_FailsAllPendingCommands()
	{
		var sentPayload = new TaskCompletionSource<string>();
		var socket = Substitute.For<WebSocket>();
		socket.State.Returns(WebSocketState.Open);
		socket.SendAsync(
			Arg.Any<ArraySegment<byte>>(),
			Arg.Any<WebSocketMessageType>(),
			Arg.Any<bool>(),
			Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				var segment = ci.ArgAt<ArraySegment<byte>>(0);
				sentPayload.TrySetResult(Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count));
				return Task.CompletedTask;
			});
		socket.CloseAsync(
			Arg.Any<WebSocketCloseStatus>(),
			Arg.Any<string?>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var session = CreateSession(socket);
		var command = new CommandRequest { Action = "MoveMouse", X = 1, Y = 2 };

		// Long timeout so the command stays pending until we close
		var sendTask = session.SendCommandAsync(command, TimeSpan.FromSeconds(30), CancellationToken.None);

		// Wait until the message has actually been sent (command is registered in _pendingCommands)
		await sentPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

		// CloseAsync must fail all pending commands
		await session.CloseAsync("shutting down", CancellationToken.None);

		var completion = await sendTask.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.NotNull(completion.Error);
		Assert.Contains("shutting down", completion.Error.Message, StringComparison.OrdinalIgnoreCase);
	}

	// ── SendCommandAsync timeout ──────────────────────────────────────────────

	[Fact]
	public async Task SendCommandAsync_Timeout_ThrowsTimeoutException()
	{
		var socket = CreateOpenSocketMock();
		var session = CreateSession(socket);
		var command = new CommandRequest { Action = "MoveMouse", X = 1, Y = 2 };

		await Assert.ThrowsAsync<TimeoutException>(
			() => session.SendCommandAsync(command, TimeSpan.FromMilliseconds(50), CancellationToken.None));
	}

	// ── IsOpen ────────────────────────────────────────────────────────────────

	[Fact]
	public void IsOpen_WhenSocketOpen_ReturnsTrue()
	{
		var socket = CreateOpenSocketMock();
		var session = CreateSession(socket);
		Assert.True(session.IsOpen);
	}

	[Fact]
	public void IsOpen_WhenSocketClosed_ReturnsFalse()
	{
		var socket = Substitute.For<WebSocket>();
		socket.State.Returns(WebSocketState.Closed);
		var session = CreateSession(socket);
		Assert.False(session.IsOpen);
	}
}
