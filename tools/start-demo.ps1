<#
.SYNOPSIS
  One-click launcher for the BLADE demo: checks Ollama and the models, builds, starts the server,
  waits until the models are warm, then opens the desktop app.

.DESCRIPTION
  Run it with Start-BLADE.cmd in the repository root (double-click).
  - Reuses a server that is already running on the configured port.
  - Logs server output to logs\server-<timestamp>.log (gitignored).
  - While the app is open, re-warms the models every 20 minutes so they are never unloaded mid-demo.
  - When you close the app, stops the server it started and exits.

.PARAMETER NoBuild
  Skip "dotnet build" and use the existing binaries.

.PARAMETER Stop
  Stop a BLADE server (and the app) left running, then exit.
#>
param(
    [switch]$NoBuild,
    [switch]$Stop
)

# Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 punctuation in scripts without a BOM.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Host.UI.RawUI.WindowTitle = "BLADE launcher"

$settings = Get-Content (Join-Path $Root "src\Blade.Server\appsettings.json") -Raw | ConvertFrom-Json
$ServerUrl = $settings.Urls.TrimEnd('/')
$OllamaUrl = $settings.Blade.AI.OllamaUrl.TrimEnd('/')
$ChatModel = $settings.Blade.AI.ChatModel
$EmbedModel = $settings.Blade.AI.EmbeddingModel
$ServerExe = Join-Path $Root "src\Blade.Server\bin\Debug\net10.0\Blade.Server.exe"
$DesktopExe = Join-Path $Root "src\Blade.Desktop\bin\Debug\net10.0-windows\Blade.Desktop.exe"
$OllamaApp = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama app.exe"

function Step($text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "    OK   $text" -ForegroundColor Green }
function Warn($text) { Write-Host "    WARN $text" -ForegroundColor Yellow }
function Fail($text) {
    Write-Host ""
    Write-Host "    FAILED: $text" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}

function Get-Json($url, $timeoutSec = 3) {
    try { return Invoke-RestMethod -Uri $url -TimeoutSec $timeoutSec } catch { return $null }
}

function Wait-Until($what, $seconds, [scriptblock]$test) {
    $deadline = (Get-Date).AddSeconds($seconds)
    # Callers print a label with -NoNewline; progress dots follow it and the line is always closed.
    while ((Get-Date) -lt $deadline) {
        $result = & $test
        if ($result) { Write-Host ""; return $result }
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 1
    }
    Write-Host ""
    Fail "$what did not become ready within $seconds seconds."
}

function Stop-Blade {
    $procs = @(Get-Process -Name "Blade.Server", "Blade.Desktop" -ErrorAction SilentlyContinue)
    foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    return $procs.Count
}

Add-Type -Namespace Blade -Name Win -MemberDefinition @"
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
"@

if ($Stop) {
    $n = Stop-Blade
    Write-Host "Stopped $n BLADE process(es)."
    Start-Sleep -Seconds 2
    exit 0
}

Write-Host "BLADE demo launcher" -ForegroundColor White
Write-Host "Repository: $Root"

# ---------------------------------------------------------------- pre-flight
Step "Pre-flight"
$battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
if ($battery -and $battery.BatteryStatus -eq 1) { Warn "Running on battery. Plug in: the GPU is much slower on battery." }
else { Ok "Power" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail ".NET SDK not found. Install the .NET 10 SDK." }
Ok ".NET SDK"

# ---------------------------------------------------------------- Ollama
Step "Ollama ($OllamaUrl)"
$tags = Get-Json "$OllamaUrl/api/tags"
if (-not $tags) {
    if (-not (Test-Path $OllamaApp)) { Fail "Ollama is not running and was not found at $OllamaApp. Start Ollama, then run this again." }
    Write-Host "    Starting Ollama" -NoNewline
    Start-Process -FilePath $OllamaApp
    $tags = Wait-Until "Ollama" 60 { Get-Json "$OllamaUrl/api/tags" }
}
Ok "Ollama is running"
$names = @($tags.models | ForEach-Object { $_.name })
foreach ($m in @($ChatModel, $EmbedModel)) {
    $present = $names | Where-Object { $_ -eq $m -or $_ -eq "$($m):latest" }
    if (-not $present) { Fail "Model '$m' is not installed. Run:  ollama pull $m" }
    Ok "Model $m"
}

# ---------------------------------------------------------------- existing processes
$serverUp = Get-Json "$ServerUrl/api/system"
$desktopRunning = @(Get-Process -Name "Blade.Desktop" -ErrorAction SilentlyContinue)

# ---------------------------------------------------------------- build
Step "Build"
if ($NoBuild) { Warn "Skipped (-NoBuild)" }
elseif ($serverUp -or $desktopRunning.Count -gt 0) { Warn "Skipped: BLADE is already running, so its files are in use. Using the existing build." }
else {
    $buildLog = & dotnet build (Join-Path $Root "Blade.slnx") -nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildLog | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
        if ((Test-Path $ServerExe) -and (Test-Path $DesktopExe)) { Warn "Build failed. Continuing with the previous build." }
        else { Fail "Build failed and there is no previous build to fall back on." }
    }
    else { Ok "Build succeeded" }
}
if (-not (Test-Path $ServerExe)) { Fail "Server not built: $ServerExe" }
if (-not (Test-Path $DesktopExe)) { Fail "Desktop app not built: $DesktopExe" }

# ---------------------------------------------------------------- data
Step "Data"
$db = Join-Path $Root "data\blade.db"
if (-not (Test-Path $db)) {
    Write-Host "    data\blade.db not found. Generating synthetic data (about 40 seconds)..."
    & dotnet run --project (Join-Path $Root "src\Blade.DataGen") -nologo
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $db)) { Fail "Data generation failed." }
}
Ok "Synthetic database: $([math]::Round((Get-Item $db).Length / 1MB, 1)) MB"

# ---------------------------------------------------------------- server
Step "Server ($ServerUrl)"
$serverProc = $null
if ($serverUp) { Ok "Already running; reusing it" }
else {
    $logs = Join-Path $Root "logs"
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $log = Join-Path $logs "server-$stamp.log"
    $serverProc = Start-Process -FilePath $ServerExe -WorkingDirectory (Join-Path $Root "src\Blade.Server") `
        -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError (Join-Path $logs "server-$stamp.err.log") -PassThru
    Write-Host "    Log: $log"
    Write-Host "    Starting" -NoNewline
    $null = Wait-Until "The BLADE server" 60 {
        if ($serverProc.HasExited) { Fail "The server exited during startup. See $log" }
        Get-Json "$ServerUrl/api/system"
    }
    Ok "Server started (process $($serverProc.Id))"
}

Write-Host "    Warming models" -NoNewline
$sys = Wait-Until "Model warm-up" 180 {
    $s = Get-Json "$ServerUrl/api/system"
    if ($s -and $s.modelsWarm) { $s } else { $null }
}
Ok "Models warm: $($sys.chatModel) + $($sys.embeddingModel) via $($sys.provider)"
Ok "$($sys.incidents) incidents, $($sys.policySections) policy sections, data as of $($sys.dataAsOf)"
if (-not $sys.local) { Warn "The server is using a CLOUD provider. The demo is meant to run on local AI." }

# ---------------------------------------------------------------- desktop
Step "Desktop app"
if ($desktopRunning.Count -gt 0) {
    Ok "Already open; bringing it to the front"
    [Blade.Win]::ShowWindow($desktopRunning[0].MainWindowHandle, 3) | Out-Null
    [Blade.Win]::SetForegroundWindow($desktopRunning[0].MainWindowHandle) | Out-Null
    $desktop = $desktopRunning[0]
}
else {
    $desktop = Start-Process -FilePath $DesktopExe -WorkingDirectory (Split-Path $DesktopExe) -PassThru
    Ok "Started"
}

Write-Host ""
Write-Host "BLADE is ready." -ForegroundColor Green
Write-Host "Keep this window open (it is minimized). It keeps the models warm and,"
Write-Host "when you close the BLADE app, stops the server it started."

# Get this console out of the way during the demo.
Start-Sleep -Seconds 2
[Blade.Win]::ShowWindow([Blade.Win]::GetConsoleWindow(), 6) | Out-Null   # 6 = minimize

# ---------------------------------------------------------------- keep warm until the app closes
$nextWarm = (Get-Date).AddMinutes(20)
while (-not $desktop.HasExited) {
    Start-Sleep -Seconds 5
    if ((Get-Date) -ge $nextWarm) {
        try { Invoke-RestMethod -Method Post -Uri "$ServerUrl/api/admin/warmup" -TimeoutSec 120 | Out-Null; Write-Host "$(Get-Date -Format HH:mm) models re-warmed" }
        catch { Write-Host "$(Get-Date -Format HH:mm) re-warm failed: $($_.Exception.Message)" }
        $nextWarm = (Get-Date).AddMinutes(20)
    }
}

if ($serverProc -and -not $serverProc.HasExited) {
    Write-Host "BLADE app closed. Stopping the server..."
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
}
