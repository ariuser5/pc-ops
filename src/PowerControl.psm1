<#
.SYNOPSIS
    PowerControl Module - Manages application lifecycle based on power source
.DESCRIPTION
    Provides functions to start, stop, and track applications automatically
    based on AC/Battery power state, with intelligent resume capabilities.
.NOTES
    Version: 1.0.0
    Author: PC-OPS Project
    Last Updated: January 2026
#>

# Module-level configuration
$script:StateDir = Join-Path $env:LOCALAPPDATA 'PCOps\State'
$script:LogDir = Join-Path $env:LOCALAPPDATA 'PCOps\Logs'

# Ensure directories exist
if (-not (Test-Path $script:StateDir)) {
    New-Item -Path $script:StateDir -ItemType Directory -Force | Out-Null
}
if (-not (Test-Path $script:LogDir)) {
    New-Item -Path $script:LogDir -ItemType Directory -Force | Out-Null
}

<#
.SYNOPSIS
    Writes a timestamped log entry
.PARAMETER Message
    The message to log
.PARAMETER Level
    Log level (INFO, WARNING, ERROR)
.PARAMETER AppName
    Application name for context
#>
function Write-PCLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        
        [Parameter()]
        [ValidateSet('INFO', 'WARNING', 'ERROR')]
        [string]$Level = 'INFO',
        
        [Parameter()]
        [string]$AppName = 'System'
    )
    
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logMessage = "[$timestamp] [$Level] [$AppName] $Message"
    
    # Write to console
    switch ($Level) {
        'ERROR' { Write-Error $logMessage }
        'WARNING' { Write-Warning $logMessage }
        default { Write-Verbose $logMessage -Verbose }
    }
    
    # Write to log file
    $logFile = Join-Path $script:LogDir "pc-ops-$(Get-Date -Format 'yyyy-MM').log"
    try {
        Add-Content -Path $logFile -Value $logMessage -ErrorAction SilentlyContinue
    } catch {
        # Silently fail if log file is locked
    }
}

<#
.SYNOPSIS
    Gets the state file path for an application
.PARAMETER AppName
    Name of the application
.PARAMETER StateType
    Type of state file (running, ignore_exit)
#>
function Get-StateFilePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppName,
        
        [Parameter(Mandatory)]
        [ValidateSet('running', 'ignore_exit')]
        [string]$StateType
    )
    
    $sanitizedName = $AppName -replace '[^\w\-]', '_'
    $fileName = switch ($StateType) {
        'running' { ".${sanitizedName}_running_state" }
        'ignore_exit' { ".${sanitizedName}_ignore_next_exit" }
    }
    
    return Join-Path $script:StateDir $fileName
}

<#
.SYNOPSIS
    Safely creates or updates a state file with retry logic
.PARAMETER Path
    Path to the state file
.PARAMETER Content
    Content to write (optional)
.PARAMETER MaxRetries
    Maximum number of retry attempts
#>
function Set-StateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        
        [Parameter()]
        [string]$Content = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),
        
        [Parameter()]
        [int]$MaxRetries = 3
    )
    
    $attempt = 0
    $success = $false
    
    while (-not $success -and $attempt -lt $MaxRetries) {
        try {
            $attempt++
            Set-Content -Path $Path -Value $Content -Force -ErrorAction Stop
            $success = $true
        } catch {
            if ($attempt -lt $MaxRetries) {
                Start-Sleep -Milliseconds (100 * $attempt)
            } else {
                throw "Failed to write state file after $MaxRetries attempts: $_"
            }
        }
    }
}

<#
.SYNOPSIS
    Safely removes a state file with retry logic
.PARAMETER Path
    Path to the state file
.PARAMETER MaxRetries
    Maximum number of retry attempts
#>
function Remove-StateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        
        [Parameter()]
        [int]$MaxRetries = 3
    )
    
    if (-not (Test-Path $Path)) {
        return $true
    }
    
    $attempt = 0
    $success = $false
    
    while (-not $success -and $attempt -lt $MaxRetries) {
        try {
            $attempt++
            Remove-Item -Path $Path -Force -ErrorAction Stop
            $success = $true
        } catch {
            if ($attempt -lt $MaxRetries) {
                Start-Sleep -Milliseconds (100 * $attempt)
            } else {
                Write-PCLog -Message "Failed to remove state file after $MaxRetries attempts: $_" -Level WARNING
                return $false
            }
        }
    }
    
    return $true
}

<#
.SYNOPSIS
    Starts an application with state tracking
.DESCRIPTION
    Starts the specified application and creates a running state file.
    Only starts if AC power is connected and the running state exists
    or this is a fresh start.
.PARAMETER AppName
    Display name of the application
.PARAMETER ExecutablePath
    Full path to the executable
.PARAMETER Arguments
    Optional command-line arguments
.PARAMETER WorkingDirectory
    Optional working directory
.PARAMETER CheckPowerSource
    If true, only starts on AC power
.EXAMPLE
    Start-AppWithTracking -AppName "uTorrent" -ExecutablePath "C:\Program Files\uTorrent\uTorrent.exe"
#>
function Start-AppWithTracking {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppName,
        
        [Parameter(Mandatory)]
        [string]$ExecutablePath,
        
        [Parameter()]
        [string]$Arguments = '',
        
        [Parameter()]
        [string]$WorkingDirectory = '',
        
        [Parameter()]
        [bool]$CheckPowerSource = $true
    )
    
    Write-PCLog -Message "Start-AppWithTracking called" -AppName $AppName
    
    # Validate executable exists
    if (-not (Test-Path $ExecutablePath)) {
        Write-PCLog -Message "Executable not found: $ExecutablePath" -Level ERROR -AppName $AppName
        throw "Executable not found: $ExecutablePath"
    }
    
    # Check power source if required
    if ($CheckPowerSource) {
        $powerStatus = (Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue)
        if ($powerStatus -and $powerStatus.BatteryStatus -ne 2) {
            # BatteryStatus: 2 = AC Power, 1 = Battery
            Write-PCLog -Message "Not on AC power - aborting start" -Level WARNING -AppName $AppName
            return
        }
    }
    
    # Get state file paths
    $runningStateFile = Get-StateFilePath -AppName $AppName -StateType 'running'
    
    # Check if process is already running
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($ExecutablePath)
    $existingProcess = Get-Process -Name $processName -ErrorAction SilentlyContinue
    
    if ($existingProcess) {
        Write-PCLog -Message "Process already running (PID: $($existingProcess.Id))" -Level WARNING -AppName $AppName
        # Ensure state file exists
        Set-StateFile -Path $runningStateFile
        return
    }
    
    # Start the process
    try {
        $startParams = @{
            FilePath = $ExecutablePath
            PassThru = $true
        }
        
        if ($Arguments) {
            $startParams.ArgumentList = $Arguments
        }
        
        if ($WorkingDirectory -and (Test-Path $WorkingDirectory)) {
            $startParams.WorkingDirectory = $WorkingDirectory
        }
        
        $process = Start-Process @startParams
        
        # Wait a moment to ensure process actually started
        Start-Sleep -Milliseconds 500
        
        if ($process.HasExited) {
            Write-PCLog -Message "Process started but immediately exited" -Level ERROR -AppName $AppName
            throw "Process failed to start"
        }
        
        # Create running state file
        Set-StateFile -Path $runningStateFile -Content "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`nPID: $($process.Id)"
        
        Write-PCLog -Message "Successfully started (PID: $($process.Id))" -AppName $AppName
        
    } catch {
        Write-PCLog -Message "Failed to start: $_" -Level ERROR -AppName $AppName
        throw
    }
}

<#
.SYNOPSIS
    Stops an application when switching to battery power
.DESCRIPTION
    Gracefully stops the application, sets an ignore flag to prevent
    state cleanup, and preserves the running state for auto-resume.
.PARAMETER AppName
    Display name of the application
.PARAMETER ProcessName
    Process name (without .exe extension)
.PARAMETER GracefulTimeout
    Seconds to wait for graceful shutdown before force kill
.EXAMPLE
    Stop-AppForBattery -AppName "uTorrent" -ProcessName "uTorrent"
#>
function Stop-AppForBattery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppName,
        
        [Parameter(Mandatory)]
        [string]$ProcessName,
        
        [Parameter()]
        [int]$GracefulTimeout = 10
    )
    
    Write-PCLog -Message "Stop-AppForBattery called" -AppName $AppName
    
    # Get state file paths
    $ignoreExitFile = Get-StateFilePath -AppName $AppName -StateType 'ignore_exit'
    $runningStateFile = Get-StateFilePath -AppName $AppName -StateType 'running'
    
    # Check if process is running
    $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    
    if (-not $process) {
        Write-PCLog -Message "Process not running - nothing to stop" -Level WARNING -AppName $AppName
        # Clean up ignore flag if it exists
        Remove-StateFile -Path $ignoreExitFile
        return
    }
    
    # Set ignore_next_exit flag BEFORE stopping
    try {
        Set-StateFile -Path $ignoreExitFile -Content "Battery switch: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        Write-PCLog -Message "Set ignore_next_exit flag" -AppName $AppName
    } catch {
        Write-PCLog -Message "Failed to set ignore flag: $_" -Level ERROR -AppName $AppName
        throw
    }
    
    # Attempt graceful shutdown
    try {
        Write-PCLog -Message "Attempting graceful shutdown (PID: $($process.Id))" -AppName $AppName
        $process.CloseMainWindow() | Out-Null
        
        # Wait for graceful exit
        $waited = 0
        while (-not $process.HasExited -and $waited -lt $GracefulTimeout) {
            Start-Sleep -Seconds 1
            $waited++
            $process.Refresh()
        }
        
        # Force kill if still running
        if (-not $process.HasExited) {
            Write-PCLog -Message "Graceful shutdown timeout - force killing" -Level WARNING -AppName $AppName
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            Start-Sleep -Milliseconds 500
        }
        
        Write-PCLog -Message "Successfully stopped" -AppName $AppName
        
        # Preserve running state for resume
        if (-not (Test-Path $runningStateFile)) {
            Set-StateFile -Path $runningStateFile -Content "Preserved for resume: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        }
        
    } catch {
        Write-PCLog -Message "Failed to stop process: $_" -Level ERROR -AppName $AppName
        # Clean up ignore flag on failure
        Remove-StateFile -Path $ignoreExitFile
        throw
    }
}

<#
.SYNOPSIS
    Handles application exit cleanup
.DESCRIPTION
    Called when a process terminates (Event ID 4689).
    If ignore_next_exit flag is NOT set, this was a manual close,
    so we remove the running state to prevent auto-resume.
.PARAMETER AppName
    Display name of the application
.PARAMETER ProcessId
    Process ID from the exit event (optional, for logging)
.EXAMPLE
    Invoke-ExitCleanup -AppName "uTorrent" -ProcessId 1234
#>
function Invoke-ExitCleanup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppName,
        
        [Parameter()]
        [int]$ProcessId = 0
    )
    
    Write-PCLog -Message "Invoke-ExitCleanup called (PID: $ProcessId)" -AppName $AppName
    
    # Get state file paths
    $ignoreExitFile = Get-StateFilePath -AppName $AppName -StateType 'ignore_exit'
    $runningStateFile = Get-StateFilePath -AppName $AppName -StateType 'running'
    
    # Check if we should ignore this exit
    if (Test-Path $ignoreExitFile) {
        Write-PCLog -Message "ignore_next_exit flag found - preserving running state" -AppName $AppName
        
        # Remove the ignore flag
        Remove-StateFile -Path $ignoreExitFile
        
        Write-PCLog -Message "Cleanup complete - state preserved" -AppName $AppName
        return
    }
    
    # This was a manual close - remove running state
    Write-PCLog -Message "Manual close detected - removing running state" -AppName $AppName
    
    if (Remove-StateFile -Path $runningStateFile) {
        Write-PCLog -Message "Running state removed - app will not auto-resume" -AppName $AppName
    } else {
        Write-PCLog -Message "Failed to remove running state" -Level ERROR -AppName $AppName
    }
}

<#
.SYNOPSIS
    Checks if an application should auto-resume
.DESCRIPTION
    Helper function to check if the running state file exists
.PARAMETER AppName
    Display name of the application
.EXAMPLE
    Test-ShouldResume -AppName "uTorrent"
#>
function Test-ShouldResume {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppName
    )
    
    $runningStateFile = Get-StateFilePath -AppName $AppName -StateType 'running'
    return (Test-Path $runningStateFile)
}

# Export module members
Export-ModuleMember -Function @(
    'Start-AppWithTracking',
    'Stop-AppForBattery',
    'Invoke-ExitCleanup',
    'Test-ShouldResume',
    'Write-PCLog'
)
