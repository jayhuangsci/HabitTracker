# PromptCrew HTTP API

API 与旧训练服务独立，默认 `http://127.0.0.1:5283`。运行 `dotnet run --project src/PromptCrew.Api` 后，可在开发环境访问 `/swagger` 与 `/swagger/v1/swagger.json`。枚举使用下表中的字符串。

## 认证和项目权限

所有 `/api` 路由接受 `X-Api-Key: <key>`，也接受 Bearer。服务首次启动在数据目录创建 `admin.key`。管理员可创建项目和签发/撤销项目密钥；Agent 使用限定在 1–32 个项目的密钥。数据库只保存项目密钥的 SHA-256 摘要，签发响应的原文只返回一次。

项目密钥具有授权项目的读写权限；暂无只读权限或员工级权限。授权范围之外返回 403；指定项目中不存在的员工、链或轮次返回 404。员工名称在同一项目内不区分大小写唯一。

## 路由

以下 `p`、`c`、`e`、`r`、`id` 均是服务返回的 UUID。

| 方法 | 路径 | 内容 |
| --- | --- | --- |
| GET | `/health` | 无需密钥的连接检查，不返回业务数据 |
| GET | `/api/session` | 当前凭据类型、名称、允许项目 |
| GET | `/api/employee-templates` | 四个游戏角色模板 |
| GET / POST | `/api/projects` | 列出可访问项目 / 创建项目 |
| GET / PUT | `/api/projects/p` | 读取 / 更新项目 |
| GET / POST | `/api/projects/p/employees` | 列出 / 创建员工 |
| GET / PUT | `/api/projects/p/employees/e` | 读取 / 更新员工 |
| GET / POST | `/api/projects/p/chains` | 列出 / 创建任务链 |
| GET / PUT | `/api/projects/p/chains/c` | 读取 / 更新任务链 |
| POST | `/api/projects/p/chains/c/rounds` | 保存不可变输入 |
| GET | `/api/projects/p/rounds/r` | 输入、快照和全部输出历史 |
| GET | `/api/projects/p/rounds/by-request/id` | 按输入请求 ID 查询 |
| POST | `/api/projects/p/rounds/r/results` | 追加输出版本 |
| GET | `/api/projects/p/rounds` | 搜索，见下文 |
| GET | `/api/projects/p/chains/c/graph` | 所有节点、父节点、引用与修订关系 |
| GET | `/api/projects/p/chains/c/context` | 当前员工、祖先链及显式引用 |
| GET | `/api/projects/p/compare?left=r&right=r2` | 原始输入 / 最新输出逐行对比 |
| GET / POST | `/api/access-keys` | 管理员列出描述 / 签发项目密钥 |
| POST | `/api/access-keys/id/revoke` | 管理员撤销密钥 |

不提供修改轮次输入、修改父节点或删除记录的路由。修订输入通过新轮次的 `revisesRoundId` 关联；结果修正再次 POST results。

对比接口可增加 `leftVersion`、`rightVersion` 指定输出历史版本；省略时使用最新结果，v1 表示尚未输出。两个轮次 ID 可以相同，以直接比较该轮 v2 和 v3 等历史结果。不存在的版本返回 404，响应包含实际采用的输出版本号。

## 请求字段

**项目：** `name` 必填且最多 120 字符；`goal`、`constraints` 最多 10,000；`background` 最多 30,000；`status` 为 Active / Paused / Archived。更新须带 `expectedVersion`。

**员工：** `name`、`role` 必填且最多 100；`responsibilities` 必填且最多 10,000；`basePrompt` 必填且最多 30,000；`skills` 最多 24 项，每项 120；`isActive` 默认 true。更新须带 `expectedVersion`。关联 Skills 只保存名称。

**任务链：** `name` 必填最多 120；`goal` 必填最多 10,000；`acceptanceCriteria` 最多 10,000；`status` 为 Active / Completed / Archived。更新须带 `expectedVersion`。追加轮次也会增加链版本，请先重新读取再编辑配置。

**输入：**

```json
{
  "clientRequestId": "客户端生成的 UUID",
  "employeeId": "本项目的员工 UUID",
  "expectedEmployeeVersion": 1,
  "parentRoundId": null,
  "revisesRoundId": null,
  "input": "实际采用的可见业务提示词，保留空格和换行",
  "model": "实际模型名称，未知时可留空",
  "source": "Agent",
  "referenceRoundIds": [],
  "tags": ["战斗"],
  "notes": ""
}
```

`source` 为 Manual / Agent。Agent 必须携带实际使用的员工定义版本 `expectedEmployeeVersion`；手动接口可省略，由服务快照当前定义，桌面会主动携带所选版本。员工定义不匹配返回 409。

`input` 必填，最多 100,000 字符；`model` 最多 120；`notes` 最多 10,000；`tags` 最多 24 项、每项 40；`referenceRoundIds` 最多 24 个不同 ID。父节点与修订目标须同项目、同链；引用须同项目，可跨链。项目/链/员工处于可用状态时才能新增。

**结果：**

```json
{
  "clientRequestId": "本次结果请求的新 UUID",
  "expectedVersion": 1,
  "output": "用户可见结果",
  "status": "Completed",
  "artifacts": ["成果路径或 URL"],
  "note": "修正说明"
}
```

`status` 为 Completed / Failed，不接受 PendingOutput。Completed 必须有输出；Failed 的输出为空时须提供原因。输出最多 200,000 字符，备注最多 10,000；成果最多 32 项、每项 2,048。成果是文本引用，不自动读取文件或访问 URL。

新输入为 PendingOutput、version=1；首次结果通常 version=2。每次补充递增轮次版本，历史结果保留。

## 幂等和版本冲突

输入与结果分别在项目范围内对请求 ID 建立唯一约束，客户端为每次业务写入生成新 UUID。相同请求 ID、路由和规范化载荷重试，返回现有轮次：首次 201，重放 200，响应为 `{ round, replayed }`。返回的轮次是当前记录状态，可能已经追加了后续结果。

同 ID 携带不同内容或不同目标返回 `409 request_id_conflict`。原始输入/输出保留空白；标签等集合及可选名称按服务规则规范化。为避免误判，重试始终保留完整原请求文件。员工后续改名、项目后续关闭均不会使已成功请求的相同重放再次创建数据。

更新项目、员工、链及追加结果需要 `expectedVersion`。多个调用方读到 v1 时，只有一个更新成功；其他返回 `409 version_conflict`。调用方应读取并比较最新数据，再决定新请求内容，不能盲目覆盖。

错误使用 ProblemDetails，包含 `code` 与 `detail`。其他常见状态：400 校验失败、401 密钥缺失/无效、403 项目越权、404 归属不符或不存在、413 上下文过大。

## 搜索和上下文

搜索参数：`q`（输入、轮次备注和全部历史输出），`employeeId`、`chainId`、`tag`（精确标签）、`status`、`skip`、`take`（1–200，默认 50）。返回 `items/total/skip/take`，按创建时间降序。

上下文参数：`employeeId` 必填，`roundId` 为拟继续的父节点，首轮省略；可重复 `referenceId`，用于尚未保存的新提示词引用，例如：

```text
/api/projects/p/chains/c/context?employeeId=e&roundId=r&referenceId=asset1&referenceId=asset2
```

返回项目、当前员工、当前链、根到父节点的 `ancestors`、去重的 `references`、可读 `compiledContext`。引用只解析一层明确关系，不递归抓取所有关联内容；祖先自身的显式引用也会合并。历史 DTO 包含当时员工快照，当前员工定义不替代历史快照。上限 256 个祖先和 500,000 字符，超出返回 413。

## 可直接运行的 PowerShell 示例

在源码演示桌面保持打开时执行。此例为演示项目新增一条独立任务链和一轮记录，不调用模型：

```powershell
$base = 'http://127.0.0.1:5283'
$keyPath = Join-Path $PWD '.local/promptcrew-demo/admin.key'
$headers = @{ 'X-Api-Key' = [IO.File]::ReadAllText($keyPath).Trim() }
$p = @(Invoke-RestMethod "$base/api/projects" -Headers $headers)[0]
$e = @(Invoke-RestMethod "$base/api/projects/$($p.id)/employees" -Headers $headers)[0]
$c = Invoke-RestMethod "$base/api/projects/$($p.id)/chains" -Headers $headers -Method Post -ContentType 'application/json' -Body (
  @{name='API 验收'; goal='验证一轮记录与输出'} | ConvertTo-Json
)
$request = @{
  clientRequestId=[Guid]::NewGuid().ToString()
  employeeId=$e.id; expectedEmployeeVersion=$e.version
  input='请列出战斗原型的三个可验证完成标准。'
  source='Agent'; model='人工调用示例'; tags=@('API')
}
New-Item -ItemType Directory .local -Force | Out-Null
$json = $request | ConvertTo-Json -Depth 10
$json | Set-Content .local/api-round.json -Encoding utf8NoBOM
$url = "$base/api/projects/$($p.id)/chains/$($c.id)/rounds"
$saved = Invoke-RestMethod $url -Headers $headers -Method Post -ContentType 'application/json' -Body $json
# 同一请求重试，replayed 为 true，记录 ID 不变：
$retried = Invoke-RestMethod $url -Headers $headers -Method Post -ContentType 'application/json' -Body $json
$result = @{
  clientRequestId=[Guid]::NewGuid().ToString(); expectedVersion=$saved.round.version
  output='护盾溢出正确；能量不足拒绝行动；胜负条件可复现。'
  status='Completed'; artifacts=@(); note=''
} | ConvertTo-Json
Invoke-RestMethod "$base/api/projects/$($p.id)/rounds/$($saved.round.id)/results" -Headers $headers -Method Post -ContentType 'application/json' -Body $result
```

POSIX HTTP 调用使用相同 JSON：

```sh
curl --fail-with-body -H "X-Api-Key: $PROMPTCREW_KEY" \
  -H 'Content-Type: application/json' --data-binary @round.json \
  "$PROMPTCREW_API_URL/api/projects/$PROJECT_ID/chains/$CHAIN_ID/rounds"
```

变量由调用宿主设置，凭据不放进请求正文或版本控制。完整创建团队、交接、分支和冲突演示见 `scripts/Test-PromptCrewAgent.ps1`。
