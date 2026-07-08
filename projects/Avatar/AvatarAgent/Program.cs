using AvatarAgent.Services;

if (!OperatingSystem.IsWindows())
{
	throw new PlatformNotSupportedException("AvatarAgent runs on Windows only.");
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddAvatarAgentHost(args);

await builder.Build().RunAsync();
