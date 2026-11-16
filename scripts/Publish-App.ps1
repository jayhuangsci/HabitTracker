[CmdletBinding()]
param([switch]$SelfContained, [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifactDirectory = Join-Path $projectRoot 'artifacts'
$packageDirectory = Join-Path $artifactDirectory "package-$Runtime-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
$includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
& dotnet publish (Join-Path $projectRoot 'HabitTracker\HabitTracker.csproj') -c Release -r $Runtime --self-contained $includeRuntime -o (Join-Path $packageDirectory 'desktop') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
& dotnet publish (Join-Path $projectRoot 'src\HabitTracker.Api\HabitTracker.Api.csproj') -c Release -r $Runtime --self-contained $includeRuntime -o (Join-Path $packageDirectory 'api') --nologo
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-Package.ps1') -Destination (Join-Path $packageDirectory 'Run.ps1')
$runtimeNote = if ($SelfContained) { 'The .NET runtime is included.' } else { 'Install both the .NET 10 Desktop Runtime and ASP.NET Core 10 Runtime before running.' }
@"
HabitTracker - FORM strength journal

$runtimeNote
Open PowerShell in this folder and run:
  .\Run.ps1 -Demo    (sample journal)
  .\Run.ps1          (personal journal)

Data and logs are stored under %LOCALAPPDATA%\HabitTracker.
Close the desktop app to stop its local API service.
Source and guide: https://github.com/jayhuangsci/HabitTracker
"@ | Set-Content -LiteralPath (Join-Path $packageDirectory 'README.txt') -Encoding utf8
$archive = Join-Path $artifactDirectory "HabitTracker-$Runtime.zip"
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -Force
Write-Host "Package created: $archive"
