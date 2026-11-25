param([switch]$SelfContained, [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $repo 'artifacts'
$package = Join-Path $artifacts ('promptcrew-' + $Runtime + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $package -Force | Out-Null
$projects = @{ desktop='desktop/PromptCrew.Desktop'; api='src/PromptCrew.Api'; tools='src/PromptCrew.Tools' }
foreach ($part in $projects.Keys) {
    dotnet publish (Join-Path $repo $projects[$part]) -c Release -r $Runtime --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -o (Join-Path $package $part) --nologo
    if ($LASTEXITCODE -ne 0) { throw "$part publish failed." }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-PromptCrewPackage.ps1') -Destination (Join-Path $package 'Run.ps1')
Copy-Item -LiteralPath (Join-Path $repo 'skills/promptcrew-journal') -Destination (Join-Path $package 'promptcrew-journal') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'tools/promptcrew.function.json') -Destination $package
$runtimeNote = if ($SelfContained) { '已包含 .NET 运行时。' } else { '请安装 .NET 10 Desktop Runtime 和 ASP.NET Core 10 Runtime。' }
@"
PromptCrew · AI 员工提示词工作台
$runtimeNote
使用 PowerShell 7：
  ./Run.ps1 -Demo   游戏演示
  ./Run.ps1         正式工作区
数据保存到 %LOCALAPPDATA%/PromptCrew/personal 或 demo，不读取原训练数据。
保持桌面窗口打开，Agent 才能访问启动器启动的 API。
Agent 可执行工具：tools/PromptCrew.Tools.exe invoke request.json
源码、API 与 Skill 设置：https://github.com/jayhuangsci/HabitTracker/tree/codex/promptcrew
"@ | Set-Content -LiteralPath (Join-Path $package '使用说明.txt') -Encoding utf8
$archive = Join-Path $artifacts "PromptCrew-$Runtime.zip"
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -Force
Write-Host "Package created: $archive"
