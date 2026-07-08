using AvatarController.Configuration;
using AvatarController.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var controllerOptions = builder.AddAvatarControllerHost(args);

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
	KeepAliveInterval = controllerOptions.WebSocketKeepAliveInterval
});

// Add request-logging middleware after websockets but before endpoints so handlers
// can reuse buffered request bodies when Trace logging is enabled.
app.UseMiddleware<AvatarController.Middleware.RequestLoggingMiddleware>();

app.MapControllerEndpoints();

app.Run();

public partial class Program { }
