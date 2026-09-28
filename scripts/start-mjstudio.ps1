# ============================================================
# MjStudio startup script
# Fix: "the process cannot access the file startup.log because
# it is being used by another process"
#   1. Stop old process first (release file lock)
#   2. Use unique timestamped log file name
# ============================================================
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hostDir = Join-Path $root 'src\Host\MjStudio.Host'
$outDir = Join-Path $hostDir 'bin\Debug\net10.0-windows'

# 1. Stop old process (release startup.log file lock)
$old = Get-Process -Name 'MjStudio.Host' -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "Stopping old process (PID: $($old.Id -join ', '))..."
    Stop-Process -Name 'MjStudio.Host' -Force
    Start-Sleep -Milliseconds 800
}

# 2. Build (optional)
if (-not $NoBuild) {
    Write-Host 'Building...'
    dotnet build (Join-Path $root 'MjStudio.slnx') -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Build failed, aborting.' -ForegroundColor Red
        exit 1
    }
}

# 3. Use unique timestamped log file name (avoid file lock)
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logFile = Join-Path $outDir "startup_$stamp.log"
$errFile = Join-Path $outDir "startup_$stamp.err.log"

# NOTE: run the built exe directly instead of `dotnet run`.
# `dotnet run` is a parent process; when it exits it closes the redirected
# stdout handle, which crashes the child (MjStudio.Host) shortly after start.
$exe = Join-Path $outDir 'MjStudio.Host.exe'
if (-not (Test-Path $exe)) {
    Write-Host "Executable not found: $exe" -ForegroundColor Red
    exit 1
}

Write-Host "Starting MjStudio... (log: $logFile)"
Start-Process $exe -RedirectStandardOutput $logFile -RedirectStandardError $errFile -WorkingDirectory $root

Write-Host 'Started.'