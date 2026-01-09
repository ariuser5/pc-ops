<#
.SYNOPSIS
    AC Power Resume Script - Called by Task Scheduler on AC power connection
.DESCRIPTION
    Checks all configured apps and resumes those with running state files
#>

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

Write-PCLog -Message "AC Power detected - checking for apps to resume"

# Process each enabled application
foreach ($app in $config.applications | Where-Object { $_.enabled }) {
    try {
        # Check if app should be resumed
        if (Test-ShouldResume -AppName $app.name) {
            Write-PCLog -Message "Running state found - attempting resume" -AppName $app.name
            
            Start-AppWithTracking `
                -AppName $app.name `
                -ExecutablePath $app.executablePath `
                -Arguments $app.arguments `
                -WorkingDirectory $app.workingDirectory `
                -CheckPowerSource $app.checkPowerSource
        } else {
            Write-PCLog -Message "No running state - skipping" -AppName $app.name
        }
    } catch {
        Write-PCLog -Message "Error resuming app: $_" -Level ERROR -AppName $app.name
    }
}

Write-PCLog -Message "AC Power resume script completed"
