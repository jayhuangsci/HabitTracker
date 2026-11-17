#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(1024,65535)][int]$Port = 5274,
    [switch]$Demo,
    [switch]$OpenBrowser,
    [string]$DotNet
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$DotNet) {
    $portable = Join-Path $repo '.local/dotnet/dotnet.exe'
    $DotNet = if (Test-Path $portable) { $portable } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$project = Join-Path $repo 'mobile/HabitTracker.Mobile.Browser'
& $DotNet build $project --nologo
if ($LASTEXITCODE) { throw 'Mobile build failed. Install the .NET 10 wasm-tools workload; see docs/mobile.md.' }
$url = "http://127.0.0.1:$Port/"
if ($Demo) { $url += '?demo=1' }
Write-Host "FORM mobile preview: $url"
Write-Host 'Keep this terminal open. Ctrl+C stops the preview.'
if ($OpenBrowser) { Start-Process $url }
$previousUrls = $env:ASPNETCORE_URLS
try {
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
    & $DotNet run --project $project --no-build --no-launch-profile
    if ($LASTEXITCODE) { throw "Mobile preview stopped with exit code $LASTEXITCODE." }
} finally {
    $env:ASPNETCORE_URLS = $previousUrls
}
