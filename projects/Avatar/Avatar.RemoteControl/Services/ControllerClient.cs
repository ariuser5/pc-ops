using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avatar.RemoteControl.Services;

public class ControllerClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public ControllerClient(string baseUrl = "http://127.0.0.1:5050")
    {
        _baseUrl = baseUrl;
        _http = new HttpClient();
    }

    public async Task<List<AgentSummary>> GetConnectedAgentsAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/agents");
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<AgentSummary>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> SendCommandAsync(string agentId, string action, int? x = null, int? y = null, string? key = null)
    {
        try
        {
            var payload = new
            {
                agentId,
                command = new
                {
                    action,
                    x,
                    y,
                    key
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync($"{_baseUrl}/command", content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    private static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public record AgentSummary(
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("agentName")] string? AgentName,
    [property: JsonPropertyName("lastHeartbeat")] string? LastHeartbeat);
