using AvatarController.Endpoints;
using AvatarController.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("AVATAR_CONTROLLER_URLS") ?? "http://0.0.0.0:5050");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<AgentManager>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
	KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapControllerEndpoints();

app.Run();
