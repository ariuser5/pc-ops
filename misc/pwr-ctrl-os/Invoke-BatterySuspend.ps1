<#
.SYNOPSIS
    Battery Suspend Script - Called by Task Scheduler on battery power
.DESCRIPTION
    Stops all configured apps when switching to battery power
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

Write-PCLog -Message "Battery power detected - suspending apps"

# Process each enabled application
foreach ($app in $config.applications | Where-Object { $_.enabled }) {
    try {
        Stop-AppForBattery `
            -AppName $app.name `
            -ProcessName $app.processName `
            -GracefulTimeout $app.gracefulTimeout
    } catch {
        Write-PCLog -Message "Error suspending app: $_" -Level ERROR -AppName $app.name
    }
}

Write-PCLog -Message "Battery suspend script completed"
