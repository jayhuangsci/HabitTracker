[CmdletBinding()]
param([switch]$Demo, [ValidateRange(1024, 65535)][int]$Port = 5253, [string]$CaptureFile)
$ErrorActionPreference = 'Stop'
$store = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HabitTracker'
New-Item -ItemType Directory -Force -Path $store | Out-Null
$database = Join-Path $store $(if ($Demo) { 'demo.db' } else { 'training.db' })
$apiDirectory = Join-Path $PSScriptRoot 'api'
$desktopExe = Join-Path $PSScriptRoot 'desktop\HabitTracker.exe'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
try { $listener.Start() } catch { throw "Port $Port is busy. Try -Port 5254." } finally { $listener.Stop() }
$names = @('ConnectionStrings__DefaultConnection', 'DemoMode', 'HABITTRACKER_API_URL')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$apiProcess = $null
try {
    $env:ConnectionStrings__DefaultConnection = "Data Source=$database"
    $env:DemoMode = if ($Demo) { 'true' } else { 'false' }
    $env:HABITTRACKER_API_URL = "http://127.0.0.1:$Port/"
    $apiProcess = Start-Process -FilePath (Join-Path $apiDirectory 'HabitTracker.Api.exe') -ArgumentList @('--urls', "http://127.0.0.1:$Port", '--environment', 'Development') -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $store "api-$Port.log") -RedirectStandardError (Join-Path $store "api-$Port.error.log")
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($apiProcess.HasExited) { throw "The service exited. Check logs in $store." }
        try { $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 1; $ready = $health.status -eq 'healthy' } catch { }
        if ($ready) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$ready) { throw 'The service did not become ready.' }
    if ($CaptureFile) {
        $capturePath = [IO.Path]::GetFullPath($CaptureFile)
        $desktop = Start-Process -FilePath $desktopExe -ArgumentList @('--capture', "`"$capturePath`"") -Wait -PassThru
        if ($desktop.ExitCode -ne 0 -or !(Test-Path -LiteralPath $capturePath)) { throw 'The packaged desktop did not render successfully.' }
    } else {
        Start-Process -FilePath $desktopExe -Wait
    }
} finally {
    if ($apiProcess -and !$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
