---
## ⚠️🚧 **UNTESTED PROJECT** 🚧⚠️

**Nothing that is written so far has been tested.**

> **:warning: Use at your own risk! :warning:**
---


# PC-OPS: Power-Controlled Operations System

**Intelligent Windows automation for managing high-battery-drain applications based on power source.**

PC-OPS automatically suspends resource-intensive applications (like uTorrent, mining software, media servers) when your laptop switches to battery power, then seamlessly resumes them when you plug back in—but only if you didn't manually close them.

## Features

✅ **Smart Resume Logic**: Apps only restart on AC power if they were running before battery switch  
✅ **Manual Close Detection**: Won't resume apps you intentionally closed  
✅ **Multi-App Support**: Manage multiple applications from a single config file  
✅ **Production-Ready**: Robust error handling, retry logic, and comprehensive logging  
✅ **2026 Best Practices**: Uses `$env:LOCALAPPDATA` for state, modern PowerShell patterns  

## Architecture

### State Management
PC-OPS uses two flag files per application stored in `%LOCALAPPDATA%\PCOps\State`:

- **`.{appname}_running_state`**: Indicates the app should auto-resume on AC power
- **`.{appname}_ignore_next_exit`**: Temporary flag to distinguish battery-triggered exits from manual closes

### Event Triggers
Windows Task Scheduler monitors these events:

1. **Event ID 105** (Kernel-Power): AC/Battery power source changes
2. **Event ID 4689** (Security): Process termination events

### Workflow

```
┌─────────────────┐
│  AC Connected   │──> Check .running_state ──> Start app
└─────────────────┘

┌─────────────────┐
│ Battery Switch  │──> Set .ignore_next_exit ──> Kill app ──> Preserve .running_state
└─────────────────┘

┌─────────────────┐
│  App Exits      │──> Check .ignore_next_exit
└─────────────────┘      │
                         ├─ Present ──> Remove flag, keep .running_state
                         └─ Absent  ──> Delete .running_state (manual close)
```

## Prerequisites

### 1. Windows Version
- Windows 10/11 (any edition)
- PowerShell 5.1 or later

### 2. Enable Audit Process Tracking

**CRITICAL**: This is required for detecting manual app closures.

#### Method 1: Group Policy Editor (gpedit.msc)

1. Press `Win + R`, type `gpedit.msc`, press Enter
2. Navigate to:
   ```
   Computer Configuration
     └─ Windows Settings
        └─ Security Settings
           └─ Advanced Audit Policy Configuration
              └─ System Audit Policies
                 └─ Detailed Tracking
   ```
3. Double-click **Audit Process Termination**
4. Check **Success**
5. Click **OK**

#### Method 2: Local Security Policy (secpol.msc)

1. Press `Win + R`, type `secpol.msc`, press Enter
2. Navigate to:
   ```
   Advanced Audit Policy Configuration
     └─ System Audit Policies - Local Group Policy Object
        └─ Detailed Tracking
   ```
3. Double-click **Audit Process Termination**
4. Check **Success**
5. Click **OK**

#### Method 3: Command Line (Run as Administrator)

```powershell
auditpol /set /subcategory:"Process Termination" /success:enable
```

#### Verify Audit Policy

```powershell
auditpol /get /subcategory:"Process Termination"
```

Expected output:
```
Process Termination    Success
```

### 3. Administrator Privileges
Required for Task Scheduler registration (one-time setup).

## Installation

### Quick Start

1. **Clone or download this repository**:
   ```powershell
   git clone https://github.com/ariuser5/pc-ops.git
   cd pc-ops
   ```

2. **Configure your applications**:
   Edit [`config/apps.json`](config/apps.json) with your application details:
   ```json
   {
     "name": "uTorrent",
     "enabled": true,
     "executablePath": "C:\\Program Files\\uTorrent\\uTorrent.exe",
     "processName": "uTorrent",
     "arguments": "",
     "workingDirectory": "",
     "gracefulTimeout": 15,
     "checkPowerSource": true
   }
   ```

3. **Run the installer** (as Administrator):
   ```powershell
   .\Install.ps1
   ```

   The installer will:
   - ✅ Check prerequisites (Audit Process Tracking)
   - ✅ Validate configuration
   - ✅ Create state directories
   - ✅ Register 3 scheduled tasks
   - ✅ Test module loading

### Manual Installation

If you prefer manual setup:

1. **Create state directories**:
   ```powershell
   New-Item "$env:LOCALAPPDATA\PCOps\State" -ItemType Directory -Force
   New-Item "$env:LOCALAPPDATA\PCOps\Logs" -ItemType Directory -Force
   ```

2. **Register tasks** (replace `INSTALL_DIR` with your repo path):
   ```powershell
   $taskDir = "C:\path\to\pc-ops\tasks"
   
   Register-ScheduledTask -Xml (Get-Content "$taskDir\AC-Power-Resume.xml" | Out-String) -TaskName "PCOps\AC-Power-Resume"
   Register-ScheduledTask -Xml (Get-Content "$taskDir\Battery-Suspend.xml" | Out-String) -TaskName "PCOps\Battery-Suspend"
   Register-ScheduledTask -Xml (Get-Content "$taskDir\Process-Exit-Cleanup.xml" | Out-String) -TaskName "PCOps\Process-Exit-Cleanup"
   ```

## Configuration

### Application Settings

Edit [`config/apps.json`](config/apps.json):

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name for logging |
| `enabled` | boolean | Enable/disable tracking |
| `executablePath` | string | Full path to .exe |
| `processName` | string | Process name (without .exe) |
| `arguments` | string | Command-line arguments (optional) |
| `workingDirectory` | string | Working directory (optional) |
| `gracefulTimeout` | int | Seconds to wait before force kill |
| `checkPowerSource` | boolean | Only start on AC power |

### Example: Multiple Applications

```json
{
  "applications": [
    {
      "name": "uTorrent",
      "enabled": true,
      "executablePath": "C:\\Program Files\\uTorrent\\uTorrent.exe",
      "processName": "uTorrent",
      "gracefulTimeout": 15
    },
    {
      "name": "Plex Media Server",
      "enabled": true,
      "executablePath": "C:\\Program Files\\Plex\\Plex Media Server\\Plex Media Server.exe",
      "processName": "Plex Media Server",
      "gracefulTimeout": 30
    }
  ]
}
```

## Usage

### Normal Operation

PC-OPS runs automatically in the background. No user intervention needed!

1. **Start your app normally** (e.g., launch uTorrent)
2. **Unplug AC adapter** → App automatically closes, state preserved
3. **Plug back in** → App automatically resumes
4. **Manually close app** → Won't auto-resume anymore

### Manual Testing

Test the system manually:

```powershell
# Import module
Import-Module .\src\PowerControl.psm1

# Start an app with tracking
Start-AppWithTracking `
    -AppName "uTorrent" `
    -ExecutablePath "C:\Program Files\uTorrent\uTorrent.exe"

# Simulate battery switch (stops app, preserves state)
Stop-AppForBattery `
    -AppName "uTorrent" `
    -ProcessName "uTorrent"

# Check if app should resume
Test-ShouldResume -AppName "uTorrent"  # Should return True

# Simulate manual close (removes state)
Invoke-ExitCleanup -AppName "uTorrent"
Test-ShouldResume -AppName "uTorrent"  # Should return False
```

### Monitoring

#### View Logs

```powershell
# Open log directory
explorer "$env:LOCALAPPDATA\PCOps\Logs"

# Tail latest log
Get-Content "$env:LOCALAPPDATA\PCOps\Logs\pc-ops-$(Get-Date -Format 'yyyy-MM').log" -Tail 50 -Wait
```

#### Check State Files

```powershell
# List all state files
Get-ChildItem "$env:LOCALAPPDATA\PCOps\State"

# Check if app has running state
Test-Path "$env:LOCALAPPDATA\PCOps\State\.uTorrent_running_state"
```

#### View Task History

```powershell
# Open Task Scheduler
taskschd.msc

# Navigate to: Task Scheduler Library > PCOps
# Right-click task > Properties > History tab
```

## Troubleshooting

### App doesn't resume on AC power

**Check running state file exists**:
```powershell
Test-Path "$env:LOCALAPPDATA\PCOps\State\.{appname}_running_state"
```

**Manually run AC resume script**:
```powershell
.\Invoke-ACResume.ps1
```

**Check Task Scheduler logs**:
```powershell
Get-ScheduledTask -TaskName "PCOps\AC-Power-Resume" | Get-ScheduledTaskInfo
```

### App doesn't stop on battery

**Check process name is correct**:
```powershell
Get-Process | Where-Object { $_.Name -like "*torrent*" }
```

**Manually test battery suspend**:
```powershell
.\Invoke-BatterySuspend.ps1
```

### Manual close not detected

**Verify Audit Process Tracking is enabled**:
```powershell
auditpol /get /subcategory:"Process Termination"
```

**Check Security event log for Event ID 4689**:
```powershell
Get-WinEvent -LogName Security -MaxEvents 10 | Where-Object { $_.Id -eq 4689 }
```

**Manually trigger cleanup**:
```powershell
.\Invoke-ExitCleanup.ps1 -ProcessId 1234 -ProcessName "C:\Program Files\uTorrent\uTorrent.exe"
```

### State files locked

The module includes retry logic, but if issues persist:

```powershell
# Kill file locks
$stateDir = "$env:LOCALAPPDATA\PCOps\State"
Get-ChildItem $stateDir | Remove-Item -Force
```

## Uninstallation

```powershell
.\Install.ps1 -Uninstall
```

This removes all scheduled tasks. State files are preserved in case you reinstall.

To completely remove everything:
```powershell
.\Install.ps1 -Uninstall
Remove-Item "$env:LOCALAPPDATA\PCOps" -Recurse -Force
```

## Advanced Features

### Custom State Directory

Override default state location by editing module variables:

```powershell
# In PowerControl.psm1
$script:StateDir = "D:\CustomPath\State"
$script:LogDir = "D:\CustomPath\Logs"
```

### Debugging

Enable verbose logging:

```powershell
Import-Module .\src\PowerControl.psm1 -Force -Verbose
```

### Multiple Instances

PC-OPS supports multiple instances of the same app by using unique process IDs in state files.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---
