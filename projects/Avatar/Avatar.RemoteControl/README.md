# Avatar.RemoteControl

**Avatar.RemoteControl** is a lightweight CLI tool for testing and interacting with the Avatar agent control system. It connects to **AvatarController** to discover connected agents and send commands to them in real-time.

## Overview

- **Agent Discovery**: Query connected agents from AvatarController
- **Interactive Selection**: Choose which agent to control
- **Mouse Control**: Move mouse to specific coordinates, send clicks
- **Keyboard Control**: Send key commands (e.g., Enter, Space, custom keys)
- **Multi-Agent Support**: Switch between agents without restarting

## Prerequisites

- **AvatarController** running on your system (default: `http://127.0.0.1:5050`)
- At least one **AvatarAgent** connected to the controller

## Running

### Basic Usage

```powershell
dotnet run --project Avatar.RemoteControl
```

### Custom Controller URL

Set the `AVATAR_CONTROLLER_URL` environment variable:

```powershell
$env:AVATAR_CONTROLLER_URL = 'http://127.0.0.1:5075'
dotnet run --project Avatar.RemoteControl
```

Or on bash:

```bash
export AVATAR_CONTROLLER_URL='http://127.0.0.1:5075'
dotnet run --project Avatar.RemoteControl
```

## Usage Flow

1. **Launch** the CLI
2. **Select an Agent** from the list of connected agents
3. **Choose a Command**:
   - **Move Mouse**: Enter X, Y coordinates
   - **Click**: Send a click to current mouse position
   - **Send Key**: Enter a key name (e.g., `Enter`, `Space`, `A`)
4. **Return to Agent Selection** or exit

### Example Session

```
╔════════════════════════════════════════╗
║   Avatar Remote Control - Test CLI    ║
╚════════════════════════════════════════╝

[Select Agent]

  1. agent-001 (Primary Desktop)
  2. agent-002 (Secondary VM)
  3. Exit

  Choose: 1

[Controlling agent-001]

  Commands:
    1. Move Mouse
    2. Click
    3. Send Key
    4. Back to Agent Selection

  Choose: 1
    X coordinate: 500
    Y coordinate: 300
    ✓ Mouse moved to (500, 300).
```

## Architecture

- **ControllerClient**: HTTP client wrapper for AvatarController API
  - Fetches connected agents
  - Sends commands (MoveMouse, Click, SendKey)
  
- **InteractiveShell**: Menu-driven CLI interface
  - Agent selection
  - Command dispatch loop
  - User feedback and error handling

## Supported Commands

| Command | Parameters | Example |
|---------|-----------|---------|
| `MoveMouse` | `x`, `y` | MoveMouse(500, 300) |
| `Click` | None | Click() |
| `SendKey` | `key` | SendKey("Enter") |

## Troubleshooting

- **No agents connected**: Ensure AvatarController and at least one AvatarAgent are running
- **Connection refused**: Verify AvatarController is running on the correct URL
- **Invalid command**: Follow the on-screen prompts; all input is validated

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `AVATAR_CONTROLLER_URL` | `http://127.0.0.1:5050` | AvatarController endpoint |
