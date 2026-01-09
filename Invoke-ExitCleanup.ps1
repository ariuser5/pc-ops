<#
.SYNOPSIS
    Exit Cleanup Script - Called by Task Scheduler on process termination
.DESCRIPTION
    Handles process exit events to detect manual closures
.PARAMETER ProcessId
    Process ID from Event Data
.PARAMETER ProcessName
    Process name from Event Data
#>

param(
    [Parameter(Mandatory)]
    [string]$ProcessId,
    
    [Parameter(Mandatory)]
    [string]$ProcessName
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Import module
Import-Module (Join-Path $ScriptDir 'src\PowerControl.psm1') -Force

# Load configuration
$configPath = Join-Path $ScriptDir 'config\apps.json'
if (-not (Test-Path $configPath)) {
    Write-PCLog -Message "Configuration file not found: $configPath" -Level ERROR
    exit 1
}

try {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
} catch {
    Write-PCLog -Message "Failed to parse configuration: $_" -Level ERROR
    exit 1
}

# Extract executable name from full path
$exeName = [System.IO.Path]::GetFileNameWithoutExtension($ProcessName)

# Find matching application
$matchedApp = $config.applications | Where-Object { 
    $_.enabled -and $_.processName -eq $exeName 
} | Select-Object -First 1

if (-not $matchedApp) {
    # Not a monitored process - exit silently
    exit 0
}

Write-PCLog -Message "Process exit detected (PID: $ProcessId)" -AppName $matchedApp.name

try {
    Invoke-ExitCleanup -AppName $matchedApp.name -ProcessId $ProcessId
} catch {
    Write-PCLog -Message "Error during exit cleanup: $_" -Level ERROR -AppName $matchedApp.name
    exit 1
}
