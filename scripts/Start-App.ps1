[CmdletBinding()]
param(
    [switch]$Demo,
    [ValidateRange(1024, 65535)][int]$Port = 5253,
    [string]$CaptureDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$apiDirectory = Join-Path $projectRoot 'src\HabitTracker.Api'
$runtimeDirectory = Join-Path $projectRoot '.local'
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
$baseUrl = "http://127.0.0.1:$Port"
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
try { $listener.Start() } catch { throw "Port $Port is in use. Run this script with -Port 5254 (or another free port)." }
finally { $listener.Stop() }
& dotnet build (Join-Path $projectRoot 'HabitTracker.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See the output above.' }
$apiDll = Join-Path $apiDirectory 'bin\Release\net10.0\HabitTracker.Api.dll'
$desktopExe = Join-Path $projectRoot 'HabitTracker\bin\Release\net10.0-windows\HabitTracker.exe'
$database = if ($Demo) { Join-Path $runtimeDirectory 'demo.db' } else { Join-Path $apiDirectory 'habittracker.db' }
$names = @('ConnectionStrings__DefaultConnection', 'DemoMode', 'HABITTRACKER_API_URL')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$apiProcess = $null
try {
    $env:ConnectionStrings__DefaultConnection = "Data Source=$database"
    $env:DemoMode = if ($Demo) { 'true' } else { 'false' }
    $env:HABITTRACKER_API_URL = "$baseUrl/"
    $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$apiDll`"", '--urls', $baseUrl, '--environment', 'Development') -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimeDirectory "api-$Port.log") -RedirectStandardError (Join-Path $runtimeDirectory "api-$Port.error.log")
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($apiProcess.HasExited) { throw "The journal service exited. Check .local/api-$Port.error.log and .local/api-$Port.log." }
        try { $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 1; $ready = $health.status -eq 'healthy' } catch { }
        if ($ready) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$ready) { throw 'The journal service did not become ready.' }
    Write-Host "Journal ready: $baseUrl  |  API explorer: $baseUrl/swagger"
    if ($Demo) { Write-Host 'Using sample data in .local/demo.db. Your personal database is separate.' }
    if ($CaptureDirectory) {
        $capturePath = [IO.Path]::GetFullPath($CaptureDirectory, $projectRoot)
        New-Item -ItemType Directory -Force -Path $capturePath | Out-Null
        foreach ($page in @('Overview', 'Workout', 'History', 'Progress')) {
            $output = Join-Path $capturePath "$($page.ToLowerInvariant()).png"
            $desktop = Start-Process -FilePath $desktopExe -ArgumentList @('--page', $page, '--capture', "`"$output`"") -PassThru -Wait
            if ($desktop.ExitCode -ne 0 -or !(Test-Path -LiteralPath $output)) { throw "Could not capture the $page screen." }
        }
    } else {
        Start-Process -FilePath $desktopExe -Wait
    }
} finally {
    if ($apiProcess -and !$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
