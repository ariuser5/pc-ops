# Avatar

This folder contains the distributed Avatar runtime: a controller/agent system that provides a small, extensible transport for executing machine-level commands on Windows agents.

This README documents purpose, operation, protocol, configuration, and how to run and test the Avatar components. It intentionally focuses on what Avatar does and how to use it, not on fixed file-tree diagrams.

## Purpose and scope

Avatar is a lightweight foundation for remote control automation. Goals:
- Provide a secure, correlated asynchronous command transport between a controller and multiple agents.
- Keep the controller passive after agent registration; external control happens through HTTP APIs.
- Make protocol and command shapes easy to extend for future features (screenshots, OCR, AI orchestration) without transport changes.

Current limitations:
- No AI, screenshots, OCR, clipboard sync, or file transfer implemented yet.

## Architecture (high level)

- AvatarController: ASP.NET Core minimal API that hosts WebSocket endpoints for agent registration and an HTTP API for external callers.
- AvatarAgent: Windows worker that connects outbound to the controller, executes Win32 input commands, and reports results.
- Avatar.Shared: shared DTOs, enums, and serialization helpers used by both sides.

## Quick start

Build:

```powershell
dotnet build .\projects\Avatar\Avatar.sln
```

Run controller:

```powershell
dotnet run --project .\projects\Avatar\AvatarController\AvatarController.csproj
```

Run agent:

```powershell
dotnet run --project .\projects\Avatar\AvatarAgent\AvatarAgent.csproj
```

Use alternate local ports by setting environment variables before running:

```powershell
$env:AVATAR_CONTROLLER_URLS='http://127.0.0.1:5075'
$env:AVATAR_CONTROLLER_WS_URL='ws://127.0.0.1:5075/ws'
```

## Protocol

Messages travel in a small JSON envelope with `type`, optional `requestId`, and optional `payload`.

Message types include: `register`, `command`, `result`, `error`, `heartbeat`.

Commands are typed with an `action` string and action-specific payload fields (e.g., `x`, `y`, `text`, `keys`, `delta`). The shared `CommandRequest` class validates inputs and enforces required fields per action.

Correlation:
- Controller generates a unique `requestId` for each outgoing `command` and awaits a matching `result` or `error` with the same `requestId`.
- If no response arrives before the configured timeout, the controller returns `504 Gateway Timeout` to the HTTP caller.

## HTTP API

- `GET /health` — controller runtime health and settings.
- `GET /agents` — list currently connected agents and their last heartbeat.
- `POST /command` — send a command to a specified agent; returns the correlated result or mapped HTTP error.

`POST /command` status mapping examples:
- `200` — agent returned `result` (success)
- `400` — validation errors
- `404` — agent not connected
- `502` — agent returned an `error` envelope
- `503` — agent unavailable
- `504` — command timed out waiting for agent response

Quick request examples (copy-paste)

1) GET /health

- curl (Windows/macOS/Linux):

  curl.exe http://127.0.0.1:5050/health

- PowerShell:

  Invoke-RestMethod -Uri 'http://127.0.0.1:5050/health' -Method Get

2) GET /agents

- curl:

  curl.exe http://127.0.0.1:5050/agents

- PowerShell:

  Invoke-RestMethod -Uri 'http://127.0.0.1:5050/agents' -Method Get

3) POST /command — send MoveMouse (example)

- curl:

  curl.exe -X POST http://127.0.0.1:5050/command -H "Content-Type: application/json" -d "{\"agentId\":\"WORK-LAPTOP\",\"command\":{\"action\":\"MoveMouse\",\"x\":500,\"y\":300}}"

- PowerShell (recommended for Windows):

  $body = @{ agentId = 'WORK-LAPTOP'; command = @{ action = 'MoveMouse'; x = 500; y = 300 } } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Uri 'http://127.0.0.1:5050/command' -Method Post -ContentType 'application/json' -Body $body

- Postman: create a POST request to http://127.0.0.1:5050/command, set Body → raw → JSON, paste the same JSON payload, send.

4) POST /command — agent not found (returns 404)

- curl:

  curl.exe -X POST http://127.0.0.1:5050/command -H "Content-Type: application/json" -d "{\"agentId\":\"UNKNOWN\",\"command\":{\"action\":\"MoveMouse\",\"x\":1,\"y\":1}}"

5) POST /command — validation error (returns 400)

- curl (missing coordinates for MoveMouse):

  curl.exe -X POST http://127.0.0.1:5050/command -H "Content-Type: application/json" -d "{\"agentId\":\"WORK-LAPTOP\",\"command\":{\"action\":\"MoveMouse\"}}"

Notes:
- Replace host/port with values from `AVATAR_CONTROLLER_URLS` if using non-default ports.
- For Windows PowerShell, prefer `Invoke-RestMethod` or `Invoke-WebRequest` for convenient JSON handling.

## Configuration

Standard .NET configuration is used (appsettings + environment variables). Key settings:
- `AvatarController:Urls`
- `AvatarController:HeartbeatIntervalSeconds`
- `AvatarController:HeartbeatTimeoutSeconds`
- `AvatarController:CommandTimeoutSeconds`
- `AvatarAgent:ControllerUrl`
- `AvatarAgent:AgentId`, `Hostname`, `Version`

Environment variable examples:
- `AVATAR_CONTROLLER_URLS`
- `AVATAR_CONTROLLER_COMMAND_TIMEOUT_SECONDS`
- `AVATAR_CONTROLLER_WS_URL`

## Testing

A test project exists at `projects/Avatar/Avatar.Tests` with unit and integration coverage for:
- Protocol serialization/validation
- AgentSession and AgentManager behavior
- Controller endpoints and command dispatch mapping

Run tests:

```powershell
dotnet test .\projects\Avatar\Avatar.Tests\Avatar.Tests.csproj
```

## Logging and diagnostics

Logging configuration follows the standard .NET `Logging:LogLevel` section in appsettings (for example `Logging:LogLevel:Default`) and supports per-category overrides (e.g. `Logging:LogLevel:Microsoft.AspNetCore`).

Command-line overrides are still supported:
- `--log-level <Level>` — sets the minimum runtime log level (Trace/Debug/Information/Warning/Error/Critical/None)
- `--verbose` — shortcut that sets the minimum level to `Trace`

Use `--verbose` when you want trace-level diagnostics such as raw envelope payloads and heartbeat activity.
## Development notes

- The controller is passive after agent registration. Heartbeats are controller-driven and agents must respond. Stale sessions are removed automatically.
- The transport is envelope-based JSON to make forward-compatible changes straightforward.
- Consider adding a typed command catalog and separate payload DTOs per capability family as the feature set grows (screenshots, OCR, clipboard, file-transfer).

## Contributing

Open issues or PRs for bugs and enhancements. Keep documentation focused on purpose, operation, and usage (avoid fixed file-tree diagrams in docs).

---

For legacy notes and historical prototypes, see project folders at the repository root. If you need to reference an older standalone agent, search the repo history rather than relying on live files.