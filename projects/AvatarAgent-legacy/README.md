# AvatarAgent

Lightweight Windows-only remote control service built with ASP.NET Core Minimal API and a WebSocket command channel.

The service listens on the local network and exposes a WebSocket endpoint for mouse and keyboard control. It is intentionally kept simple, unauthenticated, and easy to extend.

## Project layout

```text
AvatarAgent/
├── Program.cs
├── Models/
│   └── CommandRequest.cs
├── Services/
│   ├── CommandExecutor.cs
│   └── ICommandExecutor.cs
├── Endpoints/
│   └── WebSocketEndpoints.cs
├── Win32/
│   ├── KeyboardController.cs
│   └── MouseController.cs
└── README.md
```

## Runtime

- Target framework: `.NET 8` (`net8.0-windows`)
- Framework style: ASP.NET Core Minimal API
- Default bind address: `http://0.0.0.0:5050`
- Override bind address with environment variable `AVATAR_AGENT_URLS`

## WebSocket protocol

### Connect

- Endpoint: `ws://<host>:5050/ws`

Each text message must be a JSON command payload.

### Command message format

Send JSON like:

```json
{
  "action": "MoveMouse",
  "x": 500,
  "y": 300
}
```

Supported actions:

- `MoveMouse`
- `LeftClick`
- `RightClick`
- `DoubleClick`
- `Scroll`
- `TypeText`
- `PressKey`
- `HotKey`

Examples:

```json
{
  "action": "MoveMouse",
  "x": 700,
  "y": 450
}
```

```json
{
  "action": "TypeText",
  "text": "Hello world"
}
```

```json
{
  "action": "PressKey",
  "key": "Enter"
}
```

```json
{
  "action": "HotKey",
  "keys": ["Ctrl", "Shift", "Esc"]
}
```

### Success response

```json
{
  "status": "ok",
  "action": "MoveMouse",
  "message": "Mouse moved to (700, 450).",
  "elapsedMs": 2.14
}
```

### Error response

Validation and execution failures are returned as JSON error messages over the same WebSocket connection.

```json
{
  "status": "error",
  "action": "MoveMouse",
  "error": "Invalid command request.",
  "errors": {
    "x": ["X is required for MoveMouse."],
    "y": ["Y is required for MoveMouse."]
  },
  "elapsedMs": 0.52
}
```

## Logging

The service logs:

- incoming websocket command messages
- execution time
- success and failure outcomes

Logging is provided through `Microsoft.Extensions.Logging` with the console logger.

## Build and run

Run locally:

```powershell
dotnet run --project ./projects/AvatarAgent/AvatarAgent.csproj
```

Build the project:

```powershell
dotnet build ./projects/AvatarAgent/AvatarAgent.csproj
```

Publish a self-contained executable:

```powershell
dotnet publish ./projects/AvatarAgent/AvatarAgent.csproj -c Release
```

The project is configured for self-contained `win-x64` builds.

## Notes

- This service is intentionally unauthenticated. Run it only on trusted local networks.
- Windows Firewall rules may need to be adjusted to allow inbound access on the chosen port.
- The current design leaves room for future additions such as screenshots, WebSockets, authentication, OCR, UI Automation, multi-monitor support, and LLM integrations without changing the public command endpoint shape.