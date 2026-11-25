using Microsoft.EntityFrameworkCore;
using PromptCrew.Api.Data;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public static class DemoSeeder
{
    public static async Task Seed(CrewDbContext db, CrewService service)
    {
        if (await db.Projects.AnyAsync()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var actor = AccessScope.Administrator;
        var ct = CancellationToken.None;
        var project = await service.SaveProject(null, new ProjectInput
        {
            Name = "星港 · 游戏演示", Goal = "用四名 AI 员工协作完成战斗系统原型，并比较回合制与即时制方案。",
            Background = "一款太空探索游戏。小队在废弃星港回收能源，应对守卫机器人。单次战斗约 3 分钟。",
            Constraints = "第一阶段只记录和组织提示词；不连接付费模型。原型用 C#，素材为占位图。"
        }, actor, ct);
        var team = new List<EmployeeDto>();
        foreach (var template in RoleTemplates.All)
            team.Add(await service.SaveEmployee(project.Id, null, new EmployeeInput { Name = template.Name, Role = template.Role,
                Responsibilities = template.Responsibilities, BasePrompt = template.BasePrompt, Skills = template.Skills }, actor, ct));
        var chain = await service.SaveChain(project.Id, null, new ChainInput { Name = "战斗系统原型", Goal = "设计、实现并平衡一套可验证的战斗规则。",
            AcceptanceCriteria = "行动顺序确定；伤害公式有边界条件；包含回合制、即时制两个分支。" }, actor, ct);
        async Task<RoundDto> Round(int employee, Guid? parent, string input, string output, string[] tags, Guid[]? refs = null)
        {
            var saved = await service.SaveRound(project.Id, chain.Id, new RoundInput { ClientRequestId = Guid.NewGuid(),
                EmployeeId = team[employee].Id, ParentRoundId = parent, Input = input, Model = "演示 · 人工示例", Source = EntrySource.Manual,
                Tags = tags, ReferenceRoundIds = refs ?? [] }, actor, ct);
            return (await service.SaveResult(project.Id, saved.Round.Id, new ResultInput { ClientRequestId = Guid.NewGuid(),
                ExpectedVersion = saved.Round.Version, Output = output, Artifacts = ["demo://battle/" + saved.Round.Number] }, actor, ct)).Round;
        }
        var design = await Round(0, null, "设计星港战斗的核心循环。提出统一规则，便于分别探索回合制与即时制。说明胜负条件。",
            "核心循环：选择目标 → 消耗能量行动 → 结算护盾与生命 → 敌方响应。\n胜利：击败全部守卫。失败：小队生命归零。\n保留两条探索分支：回合制强调规划；即时制强调时机。", ["战斗", "规则"]);
        var code = await Round(1, design.Id, "接手玩法方案，实现回合制 BattleState 和 ApplyAction。伤害先扣护盾，溢出扣生命。",
            "public record BattleState(int Health, int Shield, int Energy);\n\npublic static BattleState Hit(BattleState s, int damage)\n{\n    var incoming = Math.Max(0, damage);\n    var overflow = Math.Max(0, incoming - s.Shield);\n    return s with { Shield = Math.Max(0, s.Shield - incoming), Health = Math.Max(0, s.Health - overflow) };\n}\n\n交接数值策划：请评估行动成本与平均战斗时长。", ["战斗", "回合制", "程序"]);
        var balance = await Round(3, code.Id, "评估护盾、生命和能量成本。提供可复算的样例，并交接程序员修正。",
            "样例：生命 100，护盾 30，单次伤害 25。首次命中后护盾 5、生命 100；第二次命中后护盾 0、生命 80。\n普通攻击消耗 2 能量，每回合恢复 3。建议限制最大能量为 6，防止无限囤积。", ["战斗", "平衡"]);
        var fixedRound = await Round(1, balance.Id, "按数值评估加入能量上限与输入校验，并说明测试边界。",
            "能量恢复使用 Math.Clamp(energy + 3, 0, 6)。攻击前验证能量 >= 2。\n边界：零伤害、负伤害、护盾溢出、致命伤害、能量不足、恢复超过上限。", ["战斗", "修正"]);
        await service.SaveResult(project.Id, fixedRound.Id, new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = fixedRound.Version,
            Output = fixedRound.LatestOutput + "\n补充：存档载入时也需校验能量范围；原结果仍保留在版本历史中。", Note = "补充载入边界检查" }, actor, ct);
        var realTime = await Round(0, design.Id, "从同一玩法根节点探索即时制：用冷却和前摇替代回合。请明确取舍。",
            "即时制分支：普通攻击冷却 1.2 秒，前摇 0.25 秒；闪避消耗 2 能量。暂停菜单冻结模拟时间。\n取舍：操作反馈更直接，但要新增时间驱动、输入缓冲和动画同步。暂不合并回合制代码。", ["战斗", "即时制", "分支"]);
        await Round(2, realTime.Id, "为即时制方案编写护盾命中反馈与攻击前摇素材提示词，引用原始玩法方案。",
            "素材提示词：俯视视角科幻机器人，清晰轮廓，攻击前摇使用琥珀色光环，护盾命中使用青蓝色碎片，透明背景，避免文字。\n规格：4 帧前摇、6 帧命中；逐帧对齐同一中心点。", ["美术", "交接"], [design.Id]);
        await transaction.CommitAsync();
    }
}
