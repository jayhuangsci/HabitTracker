param([Parameter(Mandatory)][string]$RequestFile)
$ErrorActionPreference = 'Stop'
$toolDll = $env:PROMPTCREW_TOOLS_DLL
if (-not $toolDll) { $toolDll = Join-Path $PSScriptRoot '../../../src/PromptCrew.Tools/bin/Release/net10.0/PromptCrew.Tools.dll' }
if (-not (Test-Path -LiteralPath $toolDll -PathType Leaf)) { throw 'Build PromptCrew.Tools and configure PROMPTCREW_TOOLS_DLL with its absolute DLL path.' }
dotnet $toolDll invoke ([IO.Path]::GetFullPath($RequestFile))
exit $LASTEXITCODE
