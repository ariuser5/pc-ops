<#
.SYNOPSIS
    PC-OPS Installation Script
.DESCRIPTION
    Automates the installation and configuration of PC-OPS power management system
.PARAMETER Uninstall
    Removes all scheduled tasks and cleans up
.EXAMPLE
    .\Install.ps1
    .\Install.ps1 -Uninstall
#>

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Require Administrator privileges
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

Write-Host "PC-OPS Power Management System Installer" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Helper function to register a task
function Register-PCOpsTask {
    param(
        [string]$TaskName,
        [string]$XmlPath
    )
    
    Write-Host "Registering task: $TaskName..." -NoNewline
    
    try {
        # Read XML and replace INSTALL_DIR placeholder
        $xmlContent = Get-Content $XmlPath -Raw
        $xmlContent = $xmlContent -replace '\$\(INSTALL_DIR\)', $ScriptDir
        
        # Create temp file with updated content
        $tempXml = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $tempXml -Value $xmlContent -Encoding Unicode
        
        # Register task
        $null = Register-ScheduledTask -Xml (Get-Content $tempXml | Out-String) -TaskName $TaskName -Force
        
        # Clean up temp file
        Remove-Item $tempXml -Force
        
        Write-Host " OK" -ForegroundColor Green
    } catch {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor Red
        throw
    }
}

# Helper function to unregister a task
function Unregister-PCOpsTask {
    param(
        [string]$TaskName
    )
    
    Write-Host "Removing task: $TaskName..." -NoNewline
    
    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Host " OK" -ForegroundColor Green
        } else {
            Write-Host " NOT FOUND" -ForegroundColor Yellow
        }
    } catch {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor Red
    }
}

# Uninstall mode
if ($Uninstall) {
    Write-Host "UNINSTALLING PC-OPS" -ForegroundColor Yellow
    Write-Host ""
    
    Unregister-PCOpsTask -TaskName "PCOps\AC-Power-Resume"
    Unregister-PCOpsTask -TaskName "PCOps\Battery-Suspend"
    Unregister-PCOpsTask -TaskName "PCOps\Process-Exit-Cleanup"
    
    Write-Host ""
    Write-Host "Uninstallation complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Note: State files in $env:LOCALAPPDATA\PCOps have been preserved." -ForegroundColor Cyan
    Write-Host "To remove them manually, run: Remove-Item '$env:LOCALAPPDATA\PCOps' -Recurse -Force" -ForegroundColor Cyan
    
    exit 0
}

# Installation mode
Write-Host "INSTALLING PC-OPS" -ForegroundColor Green
Write-Host ""

# Step 1: Verify prerequisites
Write-Host "[1/5] Checking prerequisites..." -ForegroundColor Cyan

# Check if Audit Process Tracking is enabled
Write-Host "  Checking Audit Process Tracking..." -NoNewline
$auditSettings = auditpol /get /subcategory:"Process Termination" 2>&1
if ($auditSettings -match "Success") {
    Write-Host " ENABLED" -ForegroundColor Green
} else {
    Write-Host " NOT ENABLED" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  WARNING: Audit Process Tracking is not enabled!" -ForegroundColor Yellow
    Write-Host "  This is required for detecting manual app closures." -ForegroundColor Yellow
    Write-Host "  See README.md for instructions on enabling it." -ForegroundColor Yellow
    Write-Host ""
    
    $response = Read-Host "  Continue anyway? (y/N)"
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "Installation cancelled." -ForegroundColor Yellow
        exit 1
    }
}

# Step 2: Validate configuration
Write-Host ""
Write-Host "[2/5] Validating configuration..." -ForegroundColor Cyan

$configPath = Join-Path $ScriptDir 'config\apps.json'
if (-not (Test-Path $configPath)) {
    Write-Error "Configuration file not found: $configPath"
    exit 1
}

try {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $enabledApps = $config.applications | Where-Object { $_.enabled }
    
    Write-Host "  Found $($enabledApps.Count) enabled application(s):" -ForegroundColor White
    foreach ($app in $enabledApps) {
        Write-Host "    - $($app.name)" -ForegroundColor Gray
        
        if (-not (Test-Path $app.executablePath)) {
            Write-Host "      WARNING: Executable not found: $($app.executablePath)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Error "Failed to parse configuration: $_"
    exit 1
}

# Step 3: Create state directories
Write-Host ""
Write-Host "[3/5] Creating state directories..." -ForegroundColor Cyan

$stateDir = Join-Path $env:LOCALAPPDATA 'PCOps\State'
$logDir = Join-Path $env:LOCALAPPDATA 'PCOps\Logs'

if (-not (Test-Path $stateDir)) {
    New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
    Write-Host "  Created: $stateDir" -ForegroundColor Green
}

if (-not (Test-Path $logDir)) {
    New-Item -Path $logDir -ItemType Directory -Force | Out-Null
    Write-Host "  Created: $logDir" -ForegroundColor Green
}

# Step 4: Register scheduled tasks
Write-Host ""
Write-Host "[4/5] Registering scheduled tasks..." -ForegroundColor Cyan

Register-PCOpsTask -TaskName "PCOps\AC-Power-Resume" -XmlPath (Join-Path $ScriptDir 'tasks\AC-Power-Resume.xml')
Register-PCOpsTask -TaskName "PCOps\Battery-Suspend" -XmlPath (Join-Path $ScriptDir 'tasks\Battery-Suspend.xml')
Register-PCOpsTask -TaskName "PCOps\Process-Exit-Cleanup" -XmlPath (Join-Path $ScriptDir 'tasks\Process-Exit-Cleanup.xml')

# Step 5: Test module import
Write-Host ""
Write-Host "[5/5] Testing module..." -ForegroundColor Cyan

try {
    Import-Module (Join-Path $ScriptDir 'src\PowerControl.psm1') -Force
    Write-Host "  Module loaded successfully" -ForegroundColor Green
} catch {
    Write-Error "Failed to load PowerControl module: $_"
    exit 1
}

# Installation complete
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Installation Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Edit config\apps.json to configure your applications" -ForegroundColor White
Write-Host "  2. Ensure Audit Process Tracking is enabled (see README.md)" -ForegroundColor White
Write-Host "  3. Test by manually starting an app and switching power sources" -ForegroundColor White
Write-Host ""
Write-Host "Logs location: $logDir" -ForegroundColor Gray
Write-Host "State files: $stateDir" -ForegroundColor Gray
Write-Host ""
Write-Host "To uninstall, run: .\Install.ps1 -Uninstall" -ForegroundColor Gray
Write-Host ""
