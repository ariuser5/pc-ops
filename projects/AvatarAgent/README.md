# AvatarAgent

Lightweight Windows-only remote control API built with ASP.NET Core Minimal API.

The service listens on the local network and exposes a small HTTP API for mouse and keyboard control. It is intentionally kept simple, unauthenticated, and easy to extend.

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
│   └── CommandEndpoints.cs
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

## Endpoints

### `GET /health`

Response:

```json
{
  "status": "ok"
}
```

### `POST /command`

Request body:

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

Example success response:

```json
{
  "status": "ok",
  "action": "MoveMouse",
  "message": "Mouse moved to (700, 450).",
  "elapsedMs": 2.14
}
```

Invalid requests return HTTP `400` with JSON validation details.

## Logging

The API logs:

- incoming requests
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

- This API is intentionally unauthenticated. Run it only on trusted local networks.
- Windows Firewall rules may need to be adjusted to allow inbound access on the chosen port.
- The current design leaves room for future additions such as screenshots, WebSockets, authentication, OCR, UI Automation, multi-monitor support, and LLM integrations without changing the public command endpoint shape.