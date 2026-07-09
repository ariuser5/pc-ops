using Avatar.RemoteControl;
using Avatar.RemoteControl.Services;

var baseUrl = Environment.GetEnvironmentVariable("AVATAR_CONTROLLER_URL") ?? "http://127.0.0.1:5050";
var client = new ControllerClient(baseUrl);
var shell = new InteractiveShell(client);

await shell.RunAsync();
