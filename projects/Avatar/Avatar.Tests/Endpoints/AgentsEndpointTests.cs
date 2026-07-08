using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avatar.Tests.Helpers;
using AvatarController.Models;

namespace Avatar.Tests.Endpoints;

public sealed class AgentsEndpointTests : IClassFixture<AvatarControllerTestFactory>
{
	private readonly HttpClient _client;

	public AgentsEndpointTests(AvatarControllerTestFactory factory)
	{
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task GetAgents_NoConnectedAgents_ReturnsOk()
	{
		var response = await _client.GetAsync("/agents");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task GetAgents_NoConnectedAgents_ReturnsEmptyList()
	{
		var response = await _client.GetAsync("/agents");
		var body = await response.Content.ReadFromJsonAsync<AgentSummaryResponse[]>(
			new JsonSerializerOptions(JsonSerializerDefaults.Web));

		Assert.NotNull(body);
		Assert.Empty(body);
	}

	[Fact]
	public async Task GetAgents_ReturnsJsonContentType()
	{
		var response = await _client.GetAsync("/agents");

		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
	}
}
