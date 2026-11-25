param([int]$Port = 5284, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid port.' }
if (-not $NoBuild) {
    dotnet build (Join-Path $repo 'PromptCrew.slnx') -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw "Port $Port is occupied." }
$dataDir = Join-Path $repo ('.local/agent-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDir | Out-Null
$names = @('PromptCrew__DataDirectory','PromptCrew__DemoMode','PromptCrew__AdminKey','PromptCrew__AdminKeyFile','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','PROMPTCREW_API_URL','PROMPTCREW_KEY_FILE')
$saved = @{}; foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
$server = $null; $script:sequence = 0
$toolDll = Join-Path $repo 'src/PromptCrew.Tools/bin/Release/net10.0/PromptCrew.Tools.dll'
function Invoke-CrewTool([hashtable]$Request, [switch]$AllowFailure) {
    $script:sequence++
    $path = Join-Path $dataDir ('{0:00}-{1}.json' -f $script:sequence, $Request.operation)
    $Request | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    $raw = & dotnet $toolDll invoke $path; $result = $raw | ConvertFrom-Json -Depth 60
    if (-not $AllowFailure -and -not $result.ok) { throw "$($result.error.code): $($result.error.message). Request retained: $path" }
    return $result
}
try {
    $env:PromptCrew__DataDirectory = $dataDir; $env:PromptCrew__DemoMode = 'false'
    $env:PromptCrew__AdminKeyFile = Join-Path $dataDir 'admin.key'; Remove-Item Env:PromptCrew__AdminKey -ErrorAction SilentlyContinue
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $apiDll = Join-Path $repo 'src/PromptCrew.Api/bin/Release/net10.0/PromptCrew.Api.dll'
    $server = Start-Process dotnet -ArgumentList @('"' + $apiDll + '"') -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dataDir 'api.log') -RedirectStandardError (Join-Path $dataDir 'api-error.log')
    $ready = $false
    for ($i = 0; $i -lt 100; $i++) {
        if ($server.HasExited) { throw "API exited. See $dataDir/api-error.log" }
        try { $ready = (Invoke-RestMethod "$env:ASPNETCORE_URLS/health" -TimeoutSec 1).product -eq 'PromptCrew' } catch { }
        if ($ready) { break }; Start-Sleep -Milliseconds 150
    }
    if (-not $ready) { throw 'API not ready.' }
    $env:PROMPTCREW_API_URL = $env:ASPNETCORE_URLS; $env:PROMPTCREW_KEY_FILE = $env:PromptCrew__AdminKeyFile
    $project = (Invoke-CrewTool @{ operation='create_project'; body=@{ name='星港 · Agent 验收'; goal='战斗系统原型'; background='废弃星港、机器人守卫与能源回收'; constraints='仅保存可见业务内容；无付费模型调用' } }).data
    $issued = Invoke-RestMethod "$env:PROMPTCREW_API_URL/api/access-keys" -Method Post -Headers @{ 'X-Api-Key'=[IO.File]::ReadAllText($env:PROMPTCREW_KEY_FILE).Trim() } -ContentType 'application/json' -Body (@{ name='验收 Agent'; projectIds=@($project.id) } | ConvertTo-Json)
    $env:PROMPTCREW_KEY_FILE = Join-Path $dataDir 'agent.key'; [IO.File]::WriteAllText($env:PROMPTCREW_KEY_FILE, $issued.token)
    $templates = (Invoke-CrewTool @{ operation='list_templates' }).data; $team = @()
    foreach ($t in $templates) {
        $team += (Invoke-CrewTool @{ operation='create_employee'; projectId=$project.id; body=@{ name=$t.name; role=$t.role; responsibilities=$t.responsibilities; basePrompt=$t.basePrompt; skills=$t.skills } }).data
    }
    $chain = (Invoke-CrewTool @{ operation='create_chain'; projectId=$project.id; body=@{ name='战斗系统原型'; goal='完成设计、程序实现、数值评估和修正'; acceptanceCriteria='两个分支各自保留历史；输出可追溯' } }).data
    $steps = @(
        @{ employee=0; parent=-1; input='设计共同战斗规则'; output='护盾先吸收伤害；能量决定可用行动。' },
        @{ employee=1; parent=0; input='接手规则，实现回合制伤害结算'; output='damage = Math.Max(0, damage); 先扣护盾，再扣生命。' },
        @{ employee=3; parent=1; input='评估能量与伤害平衡'; output='生命 100、护盾 30，受到 50 伤害后生命 80；能量上限 6。' },
        @{ employee=1; parent=2; input='修正能量上限并交付边界说明'; output='恢复使用 Math.Clamp；覆盖零伤害、溢出与能量不足。' },
        @{ employee=0; parent=0; input='从根节点探索即时制分支'; output='普通攻击冷却 1.2 秒，前摇 0.25 秒。' },
        @{ employee=2; parent=4; input='接手即时制，编写命中反馈素材提示词'; output='青蓝色护盾碎片，琥珀色前摇光环，透明背景。' }
    )
    $rounds = @(); $firstRequest = $null
    foreach ($step in $steps) {
        $employee = $team[$step.employee]
        $parent = if ($step.parent -lt 0) { $null } else { $rounds[$step.parent].id }
        $contextRequest = @{ operation='get_context'; projectId=$project.id; chainId=$chain.id; employeeId=$employee.id }
        if ($parent) { $contextRequest.roundId = $parent }
        $context = (Invoke-CrewTool $contextRequest).data
        if ($context.employee.id -ne $employee.id) { throw 'Employee context mismatch.' }
        $request = @{ operation='save_round'; projectId=$project.id; chainId=$chain.id; body=@{ clientRequestId=[Guid]::NewGuid().ToString(); employeeId=$employee.id; expectedEmployeeVersion=$context.employee.version; parentRoundId=$parent; input=$step.input; model='验收 · 人工可见示例'; source='Agent'; tags=@('战斗'); referenceRoundIds=@() } }
        if (-not $firstRequest) { $firstRequest = $request }
        $created = (Invoke-CrewTool $request).data.round
        $completed = (Invoke-CrewTool @{ operation='save_result'; projectId=$project.id; roundId=$created.id; body=@{ clientRequestId=[Guid]::NewGuid().ToString(); expectedVersion=$created.version; output=$step.output; status='Completed'; artifacts=@('demo://battle/' + $created.number) } }).data.round
        $rounds += $completed
    }
    $rounds[3] = (Invoke-CrewTool @{ operation='save_result'; projectId=$project.id; roundId=$rounds[3].id; body=@{ clientRequestId=[Guid]::NewGuid().ToString(); expectedVersion=$rounds[3].version; output=($rounds[3].latestOutput + "`n补充存档载入边界。"); status='Completed'; note='追加修订' } }).data.round
    $comparison = (Invoke-CrewTool @{ operation='compare_rounds'; projectId=$project.id; roundId=$rounds[3].id; otherRoundId=$rounds[3].id; leftVersion=2; rightVersion=3 }).data
    if ($comparison.leftOutputVersion -ne 2 -or $comparison.rightOutputVersion -ne 3 -or -not $comparison.outputDiff.Contains('+ 补充存档载入边界。')) { throw 'Historical output comparison failed.' }
    $replay = (Invoke-CrewTool $firstRequest).data
    if (-not $replay.replayed -or $replay.round.id -ne $rounds[0].id) { throw 'Idempotent retry failed.' }
    $firstRequest.body.input = '不同内容必须冲突'
    $conflict = Invoke-CrewTool $firstRequest -AllowFailure
    if ($conflict.ok -or $conflict.error.status -ne 409 -or $conflict.clientRequestId -ne $firstRequest.body.clientRequestId) { throw 'Conflict or retained request ID missing.' }
    $context = (Invoke-CrewTool @{ operation='get_context'; projectId=$project.id; chainId=$chain.id; employeeId=$team[1].id; roundId=$rounds[4].id; referenceIds=@($rounds[2].id) }).data
    if ($context.ancestors.Count -ne 2 -or $context.references.Count -ne 1 -or $context.compiledContext.Contains($steps[1].input)) { throw 'Branch context leaked an unreferenced sibling.' }
    $graph = (Invoke-CrewTool @{ operation='get_graph'; projectId=$project.id; chainId=$chain.id }).data
    if ($graph.nodes.Count -ne 6) { throw 'Unexpected node count.' }
    $summary = @{ ok=$true; projectId=$project.id; chainId=$chain.id; employees=$team.Count; rounds=$graph.nodes.Count; handoffs=$true; branchContext=$true; idempotency=$true; conflict=$true; historicalComparison=$true; requestFiles=$script:sequence; dataDirectory=$dataDir }
    $summary | ConvertTo-Json | Set-Content (Join-Path $dataDir 'summary.json') -Encoding utf8NoBOM
    $summary | ConvertTo-Json
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n], 'Process') }
}
