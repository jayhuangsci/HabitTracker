# HTTP 工具约定

工具读取 JSON 文件并返回 `{ "ok": true, "data": ... }`。失败为 `ok: false`，包含错误、原 `clientRequestId` 与请求文件。HTTP 详细字段以服务的 `/swagger/v1/swagger.json` 为准；仓库中的 `tools/promptcrew.function.json` 可提供给模型宿主注册函数。

查询示例：

```json
{"operation":"list_projects"}
```

`list_employees`、`list_chains` 需要 `projectId`。`get_project` 需要项目 ID；`get_employee` 增加 `employeeId`；`get_chain` 增加 `chainId`。`list_templates` 返回演示角色模板。

```json
{
  "operation": "get_context",
  "projectId": "项目 UUID",
  "chainId": "任务链 UUID",
  "employeeId": "负责员工 UUID",
  "roundId": "父轮次 UUID，首轮省略此字段",
  "referenceIds": []
}
```

`data.ancestors` 顺序为根到父节点，`data.references` 是明确引用且不在祖先链上的记录，`data.employee.version` 用于保存时验证快照。

```json
{
  "operation": "save_round",
  "projectId": "项目 UUID",
  "chainId": "任务链 UUID",
  "body": {
    "clientRequestId": "首次生成、重试不变的 UUID",
    "employeeId": "负责员工 UUID",
    "expectedEmployeeVersion": 1,
    "parentRoundId": null,
    "input": "实际使用的可见业务提示词",
    "model": "实际模型名称；未知时留空",
    "source": "Agent",
    "referenceRoundIds": [],
    "tags": ["战斗"],
    "notes": ""
  }
}
```

返回 `data.round.id`、`data.round.version` 与 `data.replayed`。继续分支时设置 `parentRoundId`；输入修订还设置 `revisesRoundId`。UUID 示例说明不能直接作为有效 ID 提交，应使用查询结果或本地生成的新请求 UUID。

```json
{
  "operation": "save_result",
  "projectId": "项目 UUID",
  "roundId": "save_round 返回的 ID",
  "body": {
    "clientRequestId": "本次结果请求的新 UUID",
    "expectedVersion": 1,
    "output": "用户可见的输出或交接说明",
    "status": "Completed",
    "artifacts": ["项目内成果路径或 URL"],
    "note": ""
  }
}
```

失败结果可用 `status: Failed`，输出为空时须在 `note` 中说明原因。`expectedVersion` 取最新 GET 或上次保存响应，勿假设固定为 1。

其他操作：`get_round`（项目/轮次）、`get_by_request`（项目/clientRequestId）、`get_graph`（项目/链）、`search_rounds`（项目，query 内可带 text/employeeId/chainId/tag/status/skip/take）、`compare_rounds`（项目、roundId、otherRoundId）。

`compare_rounds` 可增加 `leftVersion` / `rightVersion` 指定历史输出版本，省略使用最新输出；两个轮次 ID 相同即可比较同一轮的历史结果。v1 表示尚未输出。

修改目录使用 `create_project/update_project`、`create_employee/update_employee`、`create_chain/update_chain`。对象数据放 `body`，更新需 `expectedVersion`；创建项目要求管理员密钥，项目密钥可以在授权项目中创建员工、任务链和记录。
