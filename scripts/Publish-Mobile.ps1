#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Browser','Android')][string]$Target = 'Browser',
    [string]$AndroidSdkDirectory,
    [string]$JavaSdkDirectory,
    [string]$DotNet
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$DotNet) {
    $portable = Join-Path $repo '.local/dotnet/dotnet.exe'
    $DotNet = if (Test-Path $portable) { $portable } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$artifacts = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if ($Target -eq 'Browser') {
    $output = Join-Path $artifacts 'mobile-browser'
    & $DotNet publish (Join-Path $repo 'mobile/HabitTracker.Mobile.Browser') -c Release -o $output --nologo
    if ($LASTEXITCODE) { throw 'Browser publish failed.' }
    $package = Join-Path $artifacts 'FORM-mobile-browser.zip'
    Compress-Archive -Path (Join-Path $output 'wwwroot/*') -DestinationPath $package -Force
    Write-Host "Static browser files: $output/wwwroot"
    Write-Host 'Serve these files over HTTP/HTTPS with application/wasm MIME support. See docs/mobile.md.'
} else {
    $project = Join-Path $repo 'mobile/HabitTracker.Mobile.MauiHybrid'
    if (!$AndroidSdkDirectory -and (Test-Path (Join-Path $repo '.local/android-sdk'))) { $AndroidSdkDirectory = Join-Path $repo '.local/android-sdk' }
    if (!$JavaSdkDirectory -and (Test-Path (Join-Path $repo '.local/jdk'))) { $JavaSdkDirectory = Join-Path $repo '.local/jdk' }
    $arguments = @('build', $project, '-f', 'net10.0-android', '-c', 'Debug', '--nologo')
    if ($AndroidSdkDirectory) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
    if ($JavaSdkDirectory) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
    & $DotNet @arguments
    if ($LASTEXITCODE) { throw 'Android build failed. See the Android prerequisites in docs/mobile.md.' }
    $source = Join-Path $project 'bin/Debug/net10.0-android/com.jayhuangsci.habittracker-Signed.apk'
    if (!(Test-Path $source)) { throw 'The expected signed debug APK was not produced.' }
    $package = Join-Path $artifacts 'FORM-android-debug.apk'
    Copy-Item -LiteralPath $source -Destination $package -Force
    Write-Host 'Standalone debug APK signed with the local development key. Store signing is separate.'
}
$builtFile = Get-Item -LiteralPath $package
[pscustomobject]@{ Path = $builtFile.FullName; Bytes = $builtFile.Length; SHA256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash }
