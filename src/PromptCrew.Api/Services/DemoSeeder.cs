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
        var art = await Round(2, realTime.Id, "为即时制方案编写护盾命中反馈与攻击前摇素材提示词，引用原始玩法方案。",
            "素材提示词：俯视视角科幻机器人，清晰轮廓，攻击前摇使用琥珀色光环，护盾命中使用青蓝色碎片，透明背景，避免文字。\n规格：4 帧前摇、6 帧命中；逐帧对齐同一中心点。", ["美术", "交接"], [design.Id]);
        var rules = await Round(0, realTime.Id, "根据首轮评估细化即时制规则，补充连击窗口和受击保护。",
            "连击窗口为前次命中后 0.4 秒；连续受击保护 0.6 秒。闪避先扣能量再进入无敌帧，失败输入不消耗能量。\n交接程序：用独立状态字段表达冷却、前摇、受击保护，便于测试。", ["玩法", "规则调整"]);
        var implementation = await Round(1, rules.Id, "实现连击窗口与受击保护，补充时间边界测试。",
            "新增 ComboDeadline 与 InvulnerableUntil，使用同一模拟时钟。测试覆盖窗口前、等于边界、超过边界和重复受击。\n交接数值：请用 1.2 秒攻击冷却重新估算伤害节奏。", ["程序", "即时制"]);
        var tuning = await Round(3, implementation.Id, "评估即时制攻击节奏，核对连击收益与能量恢复。",
            "普通攻击每 1.2 秒一次，伤害 25，基础每秒伤害约 20.8。两次连击的增益限制在 15%，避免站桩攻击压倒闪避策略。", ["数值", "评估"], [balance.Id]);
        var artRevision = await Round(2, art.Id, "统一护盾反馈和能量图标的视觉规范，修订素材提示词。",
            "护盾使用青蓝色，能量使用浅绿色，危险预警保留琥珀色。所有图标使用同一线宽与安全边距。\n素材提示词补充：避免细碎高频纹理，缩小到 32 像素时仍可识别。", ["美术", "规范修订"]);
        await Round(2, artRevision.Id, "根据缩略图检查再次完善前摇素材，保证小尺寸可读性。",
            "前摇光环改为两段清晰弧线；减少碎片数量，保留轮廓。验证 32/64/128 像素三个尺寸，交付透明背景的逐帧素材提示词。", ["美术", "素材完善"]);
        await Round(3, tuning.Id, "根据边界测试修订能量恢复参数，并记录本轮结论。",
            "能量每 2 秒恢复 1 点，上限 6 点；受击不重置恢复计时。压力测试包含连续闪避、低能量输入和暂停恢复。\n结论：保持行动选择，后续通过试玩数据调整恢复间隔。", ["数值", "参数修订"]);
        await transaction.CommitAsync();
    }
}
