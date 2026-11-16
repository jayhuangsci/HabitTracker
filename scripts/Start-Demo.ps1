[CmdletBinding()]
param([ValidateRange(1024, 65535)][int]$Port = 5253, [string]$CaptureDirectory)
& (Join-Path $PSScriptRoot 'Start-App.ps1') -Demo -Port $Port -CaptureDirectory $CaptureDirectory
