using AvatarAgent.Endpoints;
using AvatarAgent.Services;
using AvatarAgent.Win32;

if (!OperatingSystem.IsWindows())
{
	throw new PlatformNotSupportedException("AvatarAgent runs on Windows only.");
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
	Environment.GetEnvironmentVariable("AVATAR_AGENT_URLS")
	?? Environment.GetEnvironmentVariable("AGENT_URLS")
	?? "http://0.0.0.0:5050");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<MouseController>();
builder.Services.AddSingleton<KeyboardController>();
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
	KeepAliveInterval = TimeSpan.FromSeconds(120)
});

app.MapWebSocketEndpoints();

app.Run();
