param([switch]$Demo, [int]$Port = 5283)
$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid port.' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw "Port $Port is occupied." }
$dataDir = Join-Path $env:LOCALAPPDATA ('PromptCrew/' + $(if ($Demo) { 'demo' } else { 'personal' }))
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$names = @('PromptCrew__DataDirectory','PromptCrew__DemoMode','PromptCrew__AdminKey','PromptCrew__AdminKeyFile','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','PROMPTCREW_API_URL','PROMPTCREW_KEY_FILE','PROMPTCREW_SETTINGS_FILE')
$saved = @{}; foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
$server = $null
try {
    $env:PromptCrew__DataDirectory = $dataDir; $env:PromptCrew__DemoMode = $Demo.IsPresent.ToString()
    $env:PromptCrew__AdminKeyFile = Join-Path $dataDir 'admin.key'; Remove-Item Env:PromptCrew__AdminKey -ErrorAction SilentlyContinue
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $server = Start-Process -FilePath (Join-Path $PSScriptRoot 'api/PromptCrew.Api.exe') -WindowStyle Hidden -PassThru -WorkingDirectory $PSScriptRoot -RedirectStandardOutput (Join-Path $dataDir 'api.log') -RedirectStandardError (Join-Path $dataDir 'api-error.log')
    $ready = $false
    for ($i = 0; $i -lt 100; $i++) {
        if ($server.HasExited) { throw "API exited. See $dataDir/api-error.log" }
        try { $ready = (Invoke-RestMethod "$env:ASPNETCORE_URLS/health" -TimeoutSec 1).product -eq 'PromptCrew' } catch { }
        if ($ready) { break }; Start-Sleep -Milliseconds 150
    }
    if (-not $ready) { throw 'API not ready.' }
    $env:PROMPTCREW_API_URL = $env:ASPNETCORE_URLS; $env:PROMPTCREW_KEY_FILE = $env:PromptCrew__AdminKeyFile
    $env:PROMPTCREW_SETTINGS_FILE = Join-Path $dataDir 'desktop-settings.json'
    $desktop = Start-Process -FilePath (Join-Path $PSScriptRoot 'desktop/PromptCrew.Desktop.exe') -PassThru -Wait
    if ($desktop.ExitCode -ne 0) { throw 'Desktop exited with an error.' }
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n], 'Process') }
}
