using System.Net.WebSockets;
using AvatarController.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avatar.Tests.Services;

public sealed class AgentManagerTests
{
	private static AgentManager CreateManager() =>
		new AgentManager(NullLogger<AgentManager>.Instance);

	private static AgentSession CreateSession(string agentId = "agent-1", WebSocketState socketState = WebSocketState.Open)
	{
		var socket = Substitute.For<WebSocket>();
		socket.State.Returns(socketState);
		socket.SendAsync(
			Arg.Any<ArraySegment<byte>>(),
			Arg.Any<WebSocketMessageType>(),
			Arg.Any<bool>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);
		return new AgentSession(agentId, "host-" + agentId, "1.0.0", socket, NullLogger<AgentSession>.Instance);
	}

	[Fact]
	public void GetConnectedSessions_Empty_ReturnsEmptyList()
	{
		var manager = CreateManager();
		Assert.Empty(manager.GetConnectedSessions());
	}

	[Fact]
	public async Task UpsertAsync_NewAgent_AddsToSessions()
	{
		var manager = CreateManager();
		var session = CreateSession("agent-a");

		await manager.UpsertAsync(session, CancellationToken.None);

		var sessions = manager.GetConnectedSessions();
		Assert.Single(sessions);
		Assert.Equal("agent-a", sessions.First().AgentId);
	}

	[Fact]
	public async Task UpsertAsync_MultipleAgents_AllPresent()
	{
		var manager = CreateManager();
		await manager.UpsertAsync(CreateSession("agent-b"), CancellationToken.None);
		await manager.UpsertAsync(CreateSession("agent-a"), CancellationToken.None);
		await manager.UpsertAsync(CreateSession("agent-c"), CancellationToken.None);

		var sessions = manager.GetConnectedSessions();
		Assert.Equal(3, sessions.Count);
	}

	[Fact]
	public async Task GetConnectedSessions_ReturnsSortedByAgentId()
	{
		var manager = CreateManager();
		await manager.UpsertAsync(CreateSession("charlie"), CancellationToken.None);
		await manager.UpsertAsync(CreateSession("alpha"), CancellationToken.None);
		await manager.UpsertAsync(CreateSession("bravo"), CancellationToken.None);

		var ids = manager.GetConnectedSessions().Select(s => s.AgentId).ToList();
		Assert.Equal(["alpha", "bravo", "charlie"], ids);
	}

	[Fact]
	public async Task UpsertAsync_SameAgentIdTwice_ReplacesSession()
	{
		var manager = CreateManager();
		var first = CreateSession("agent-x", WebSocketState.Closed);
		var second = CreateSession("agent-x");

		await manager.UpsertAsync(first, CancellationToken.None);
		await manager.UpsertAsync(second, CancellationToken.None);

		var sessions = manager.GetConnectedSessions();
		Assert.Single(sessions);
		Assert.Equal(second.SessionId, sessions.First().SessionId);
	}

	[Fact]
	public async Task RemoveAsync_KnownAgent_RemovesFromSessions()
	{
		var manager = CreateManager();
		var session = CreateSession("agent-1", WebSocketState.Closed);
		await manager.UpsertAsync(session, CancellationToken.None);

		await manager.RemoveAsync(session, "test removal", CancellationToken.None);

		Assert.Empty(manager.GetConnectedSessions());
	}

	[Fact]
	public async Task RemoveAsync_StaleSession_DoesNotRemoveNewer()
	{
		var manager = CreateManager();
		var first = CreateSession("agent-1", WebSocketState.Closed);
		var second = CreateSession("agent-1", WebSocketState.Closed);

		await manager.UpsertAsync(first, CancellationToken.None);
		await manager.UpsertAsync(second, CancellationToken.None);

		// Removing the old session should not remove the new one
		await manager.RemoveAsync(first, "stale removal", CancellationToken.None);

		var sessions = manager.GetConnectedSessions();
		Assert.Single(sessions);
		Assert.Equal(second.SessionId, sessions.First().SessionId);
	}

	[Fact]
	public async Task TryGetSession_KnownAgent_ReturnsSession()
	{
		var manager = CreateManager();
		var session = CreateSession("agent-1");
		await manager.UpsertAsync(session, CancellationToken.None);

		var found = manager.TryGetSession("agent-1", out var retrieved);

		Assert.True(found);
		Assert.NotNull(retrieved);
		Assert.Equal(session.SessionId, retrieved.SessionId);
	}

	[Fact]
	public void TryGetSession_UnknownAgent_ReturnsFalse()
	{
		var manager = CreateManager();

		var found = manager.TryGetSession("nobody", out var retrieved);

		Assert.False(found);
		Assert.Null(retrieved);
	}

	[Fact]
	public async Task TryGetSession_CaseInsensitive_ReturnsSession()
	{
		var manager = CreateManager();
		var session = CreateSession("MyAgent");
		await manager.UpsertAsync(session, CancellationToken.None);

		Assert.True(manager.TryGetSession("myagent", out _));
		Assert.True(manager.TryGetSession("MYAGENT", out _));
	}
}
