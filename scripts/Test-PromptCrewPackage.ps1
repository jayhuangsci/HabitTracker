param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$checks = Join-Path $repo ('.local/exe-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checks | Out-Null

function Invoke-LaunchCheck([string]$Name, [string]$Exe, [bool]$Demo, [string]$PackageOverride = '') {
    $checkDir = Join-Path $checks $Name; New-Item -ItemType Directory -Path $checkDir | Out-Null
    $dataDir = Join-Path $checkDir 'data'; $captureDir = Join-Path $checkDir 'capture'
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $package $Exe))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $env:SystemRoot
    $start.Environment['PATH'] = (Join-Path $env:SystemRoot 'System32') + ';' + $env:SystemRoot
    $start.Environment.Remove('DOTNET_ROOT') | Out-Null
    $start.Environment.Remove('DOTNET_ROOT_X64') | Out-Null
    foreach ($value in @('--data-directory',$dataDir,'--capture-directory',$captureDir)) { $start.ArgumentList.Add($value) }
    if ($PackageOverride) { $start.ArgumentList.Add('--package-directory'); $start.ArgumentList.Add($PackageOverride) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw "$Name did not finish within 45 seconds." }
        if ($PackageOverride) {
            if ($process.ExitCode -ne 1 -or -not (Test-Path (Join-Path $captureDir 'launch-error.txt'))) { throw 'Incomplete-package error was not reported.' }
            return
        }
        if ($process.ExitCode -ne 0) {
            $errorFile = Join-Path $captureDir 'launch-error.txt'
            $details = if (Test-Path $errorFile) { Get-Content -LiteralPath $errorFile -Raw } else { 'No startup log was generated.' }
            throw "$Name failed: $details"
        }
        $result = Get-Content (Join-Path $captureDir 'launch-check.json') -Raw | ConvertFrom-Json
        if (-not $result.ok -or -not $result.authenticated -or -not $result.apiStopped -or $result.demo -ne $Demo) { throw "$Name verification failed." }
        if (-not (Test-Path (Join-Path $captureDir 'desktop.png'))) { throw "$Name did not open the desktop." }
        if (-not (Test-Path (Join-Path $dataDir 'promptcrew.db'))) { throw "$Name did not persist its workspace." }
        if ($result.port -eq 5283) { throw 'Launcher did not avoid the occupied default port.' }
        $probe = [Net.Sockets.TcpClient]::new()
        try {
            try { $probe.Connect('127.0.0.1', [int]$result.port) } catch [Net.Sockets.SocketException] { }
            if ($probe.Connected) { throw 'The owned API was still listening after the desktop closed.' }
        } finally { $probe.Dispose() }
        Write-Host "$Name passed: exe -> authenticated API -> desktop -> service shutdown."
    } finally { $process.Dispose() }
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 5283)
try {
    try { $listener.Start() } catch [Net.Sockets.SocketException] { }
    Invoke-LaunchCheck 'demo' 'PromptCrew-Demo.exe' $true
    Invoke-LaunchCheck 'personal' 'PromptCrew.exe' $false
    $empty = Join-Path $checks 'incomplete-package'; New-Item -ItemType Directory -Path $empty | Out-Null
    Invoke-LaunchCheck 'missing-files' 'PromptCrew.exe' $false $empty
    Write-Host "All executable package checks passed. Artifacts: $checks"
}
finally { $listener.Stop() }
