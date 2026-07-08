# Avatar Distributed Architecture

This folder contains the distributed Avatar runtime made of two independent applications and one shared protocol library.

## Projects

- `AvatarController`: ASP.NET Core Minimal API host that accepts agent websocket connections and manages connected sessions.
- `AvatarAgent`: Windows worker service that maintains an outbound websocket connection to the controller and executes commands with Win32 input APIs.
- `Avatar.Shared`: protocol contracts and serialization helpers shared by both sides.

## Solution

- `Avatar.sln`: standard Visual Studio solution containing all three projects.
- `Avatar.slnx`: XML solution file that can also be used by newer tooling.

Build everything from repository root:

```powershell
dotnet build .\projects\Avatar\Avatar.sln
```

## Protocol Flow

1. Agent connects to `ws://<controller>/ws`.
2. Agent sends a `register` envelope with `agentId`, `hostname`, and `version`.
3. Controller stores the session and can send `command` envelopes.
4. Agent executes the command and responds with `result` or `error`.
5. Both sides can exchange `heartbeat` envelopes.

Current behavior note:

- The controller currently sends a test `MoveMouse` command (`x=500`, `y=300`) immediately after a successful `register` message.

## Runtime Configuration

Controller environment variables:

- `AVATAR_CONTROLLER_URLS` (default: `http://0.0.0.0:5050`)

Agent environment variables:

- `AVATAR_CONTROLLER_WS_URL` (default: `ws://127.0.0.1:5050/ws`)
- `AVATAR_AGENT_ID` (default: machine name)
- `AVATAR_AGENT_HOSTNAME` (default: machine name)
- `AVATAR_AGENT_VERSION` (default: entry assembly version)
- `AVATAR_AGENT_RECONNECT_SECONDS` (default: `5`)

## Example Run (PowerShell)

If port `5050` is already used, pick an alternate port for both apps:

```powershell
$env:AVATAR_CONTROLLER_URLS='http://127.0.0.1:5075'
dotnet run --project .\projects\Avatar\AvatarController\AvatarController.csproj
```

In a second terminal:

```powershell
$env:AVATAR_CONTROLLER_WS_URL='ws://127.0.0.1:5075/ws'
dotnet run --project .\projects\Avatar\AvatarAgent\AvatarAgent.csproj
```

## Legacy Note

`projects/AvatarAgent` at repository root is the previous standalone implementation. The distributed architecture lives under `projects/Avatar` and should be the primary target for new work.