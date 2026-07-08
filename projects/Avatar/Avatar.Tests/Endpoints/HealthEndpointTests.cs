using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avatar.Tests.Helpers;
using AvatarController.Models;

namespace Avatar.Tests.Endpoints;

public sealed class HealthEndpointTests : IClassFixture<AvatarControllerTestFactory>
{
	private readonly HttpClient _client;

	public HealthEndpointTests(AvatarControllerTestFactory factory)
	{
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task GetHealth_ReturnsOk()
	{
		var response = await _client.GetAsync("/health");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task GetHealth_ReturnsExpectedShape()
	{
		var response = await _client.GetAsync("/health");
		var body = await response.Content.ReadFromJsonAsync<ControllerHealthResponse>(
			new JsonSerializerOptions(JsonSerializerDefaults.Web));

		Assert.NotNull(body);
		Assert.Equal("ok", body.Status);
		Assert.Equal(0, body.ConnectedAgents);
		Assert.True(body.HeartbeatIntervalSeconds > 0);
		Assert.True(body.CommandTimeoutSeconds > 0);
	}

	[Fact]
	public async Task GetHealth_ReturnsJsonContentType()
	{
		var response = await _client.GetAsync("/health");

		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
	}
}
