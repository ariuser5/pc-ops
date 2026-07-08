using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avatar.Shared.Payloads;
using Avatar.Tests.Helpers;
using AvatarController.Models;
using AvatarController.Services;
using NSubstitute;

namespace Avatar.Tests.Endpoints;

public sealed class CommandEndpointTests : IClassFixture<AvatarControllerTestFactory>
{
	private readonly AvatarControllerTestFactory _factory;
	private readonly HttpClient _client;
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private static readonly CommandRequest ValidMoveMouseCommand = new()
	{
		Action = "MoveMouse",
		X = 100,
		Y = 200
	};

	public CommandEndpointTests(AvatarControllerTestFactory factory)
	{
		_factory = factory;
		_client = factory.CreateClient();
	}

	// ── Validation (400) ─────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_MissingAgentId_Returns400()
	{
		var request = new { Command = ValidMoveMouseCommand };

		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PostCommand_MissingCommand_Returns400()
	{
		var request = new { AgentId = "agent-1" };

		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PostCommand_InvalidCommandAction_Returns400()
	{
		var request = new
		{
			AgentId = "agent-1",
			Command = new { Action = "FlyToMoon" }
		};

		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PostCommand_MoveMouse_MissingCoords_Returns400()
	{
		var request = new
		{
			AgentId = "agent-1",
			Command = new { Action = "MoveMouse" }
		};

		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	// ── Not found (404) ───────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_AgentNotFound_Returns404()
	{
		_factory.CommandService
			.SendCommandAsync("missing-agent", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.AgentNotFound("missing-agent"));

		var request = new { AgentId = "missing-agent", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// ── Success (200) ─────────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_Success_Returns200WithResult()
	{
		var completion = new AgentCommandCompletion(
			RequestId: "req-abc",
			Result: new CommandResultPayload { Status = "ok", ElapsedMs = 12.5, Message = "done" },
			Error: null);

		_factory.CommandService
			.SendCommandAsync("agent-1", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.Success("agent-1", completion));

		var request = new { AgentId = "agent-1", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var body = await response.Content.ReadFromJsonAsync<SendCommandApiResponse>(JsonOptions);
		Assert.NotNull(body);
		Assert.Equal("agent-1", body.AgentId);
		Assert.Equal("req-abc", body.RequestId);
		Assert.Equal("ok", body.Status);
		Assert.Equal(12.5, body.ElapsedMs);
	}

	// ── Timeout (504) ─────────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_Timeout_Returns504()
	{
		_factory.CommandService
			.SendCommandAsync("slow-agent", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.TimedOut("slow-agent"));

		var request = new { AgentId = "slow-agent", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
	}

	// ── Agent error (502) ─────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_AgentError_Returns502()
	{
		var completion = new AgentCommandCompletion(
			RequestId: "req-err",
			Result: null,
			Error: new ErrorPayload { Message = "agent exploded" });

		_factory.CommandService
			.SendCommandAsync("error-agent", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.AgentError("error-agent", completion));

		var request = new { AgentId = "error-agent", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
	}

	// ── Agent unavailable (503) ───────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_AgentUnavailable_Returns503()
	{
		_factory.CommandService
			.SendCommandAsync("busy-agent", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.AgentUnavailable("busy-agent", "socket is closed"));

		var request = new { AgentId = "busy-agent", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}

	// ── Normalisation ─────────────────────────────────────────────────────────

	[Fact]
	public async Task PostCommand_AgentIdWithLeadingWhitespace_NormalisesBeforeDispatch()
	{
		var completion = new AgentCommandCompletion(
			RequestId: "req-trim",
			Result: new CommandResultPayload { Status = "ok", ElapsedMs = 1 },
			Error: null);

		_factory.CommandService
			.SendCommandAsync("agent-trim", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>())
			.Returns(AgentCommandDispatchResult.Success("agent-trim", completion));

		var request = new { AgentId = "  agent-trim  ", Command = ValidMoveMouseCommand };
		var response = await _client.PostAsJsonAsync("/command", request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		await _factory.CommandService.Received(1)
			.SendCommandAsync("agent-trim", Arg.Any<CommandRequest>(), Arg.Any<CancellationToken>());
	}
}
