param(
    [switch]$Demo,
    [int]$Port = 5283,
    [string]$CaptureDirectory,
    [string]$DataDirectory,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be between 1024 and 65535.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $NoBuild) {
    dotnet build (Join-Path $repo 'PromptCrew.slnx') -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$dataDir = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } elseif ($Demo) { Join-Path $repo '.local/promptcrew-demo' } else { Join-Path $env:LOCALAPPDATA 'PromptCrew/personal' }
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$apiDll = Join-Path $repo 'src/PromptCrew.Api/bin/Release/net10.0/PromptCrew.Api.dll'
$desktopExe = Join-Path $repo 'desktop/PromptCrew.Desktop/bin/Release/net10.0-windows/PromptCrew.Desktop.exe'
$baseUrl = "http://127.0.0.1:$Port"
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw "Port $Port is occupied. Choose another -Port; this launcher will not replace an existing service." }
$names = @('PromptCrew__DataDirectory','PromptCrew__DemoMode','PromptCrew__AdminKeyFile','PromptCrew__AdminKey','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','PROMPTCREW_API_URL','PROMPTCREW_KEY_FILE','PROMPTCREW_SETTINGS_FILE')
$saved = @{}
foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
$server = $null
try {
    $env:PromptCrew__DataDirectory = $dataDir
    $env:PromptCrew__DemoMode = $Demo.IsPresent.ToString()
    $env:PromptCrew__AdminKeyFile = Join-Path $dataDir 'admin.key'
    Remove-Item Env:PromptCrew__AdminKey -ErrorAction SilentlyContinue
    $env:ASPNETCORE_URLS = $baseUrl; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $server = Start-Process dotnet -ArgumentList @('"' + $apiDll + '"') -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dataDir 'api.log') -RedirectStandardError (Join-Path $dataDir 'api-error.log')
    $ready = $false
    for ($i = 0; $i -lt 100; $i++) {
        if ($server.HasExited) { throw "API exited. See $dataDir/api-error.log" }
        try { $health = Invoke-RestMethod "$baseUrl/health" -TimeoutSec 1; $ready = $health.product -eq 'PromptCrew' } catch { }
        if ($ready) { break }; Start-Sleep -Milliseconds 150
    }
    if (-not $ready) { throw 'API did not become ready.' }
    $env:PROMPTCREW_API_URL = $baseUrl
    $env:PROMPTCREW_KEY_FILE = $env:PromptCrew__AdminKeyFile
    $env:PROMPTCREW_SETTINGS_FILE = Join-Path $dataDir 'desktop-settings.json'
    Write-Host "PromptCrew ready: $baseUrl | data: $dataDir"
    if ($CaptureDirectory) {
        $captureDir = [IO.Path]::GetFullPath($CaptureDirectory); New-Item -ItemType Directory -Path $captureDir -Force | Out-Null
        $captures = @(@{name='overview';view='overview'}, @{name='employee-history';view='employee'}, @{name='employee-profile';view='profile'}, @{name='record';view='record';tab=1}, @{name='comparison';view='record';tab=3})
        foreach ($capture in $captures) {
            $image = Join-Path $captureDir ($capture.name + '.png')
            $arguments = @('--view', $capture.view, '--capture', ('"' + $image + '"'))
            if ($capture.ContainsKey('tab')) { $arguments += @('--tab', $capture.tab) }
            $desktop = Start-Process -FilePath $desktopExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
            if ($desktop.ExitCode -ne 0) { throw "Desktop capture failed: $($capture.name)" }
        }
    } else {
        $desktop = Start-Process -FilePath $desktopExe -PassThru -Wait
        if ($desktop.ExitCode -ne 0) { throw 'Desktop exited with an error.' }
    }
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n], 'Process') }
}
