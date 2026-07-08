using AvatarAgent.Services;
using AvatarAgent.Win32;

if (!OperatingSystem.IsWindows())
{
	throw new PlatformNotSupportedException("AvatarAgent runs on Windows only.");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<MouseController>();
builder.Services.AddSingleton<KeyboardController>();
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
builder.Services.AddSingleton<AvatarAgentOptions>(sp => AvatarAgentOptions.Create(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService<ControllerConnectionService>();

await builder.Build().RunAsync();
