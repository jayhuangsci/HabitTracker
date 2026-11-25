using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptCrew.Core;

public enum ProjectState { Active, Paused, Archived }
public enum ChainState { Active, Completed, Archived }
public enum RoundState { PendingOutput, Completed, Failed }
public enum EntrySource { Manual, Agent }

public static class CrewJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
public sealed record ProjectDto(Guid Id, string Name, string Goal, string Background, string Constraints,
    ProjectState Status, int Version, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record EmployeeDto(Guid Id, Guid ProjectId, string Name, string Role, string Responsibilities,
    string BasePrompt, string[] Skills, bool IsActive, int Version, DateTime CreatedUtc);
public sealed record ChainDto(Guid Id, Guid ProjectId, string Name, string Goal, string AcceptanceCriteria,
    ChainState Status, int Version, DateTime CreatedUtc);
public sealed record EmployeeSnapshot(Guid Id, string Name, string Role, string Responsibilities,
    string BasePrompt, string[] Skills, int DefinitionVersion);
public sealed record ResultDto(Guid Id, Guid ClientRequestId, int Version, string Output, RoundState Status,
    string[] Artifacts, string Note, DateTime CreatedUtc);
public sealed record RoundDto(Guid Id, Guid ProjectId, Guid ChainId, Guid EmployeeId, Guid ClientRequestId,
    Guid? ParentRoundId, Guid? RevisesRoundId, int Number, string Input, EmployeeSnapshot EmployeeSnapshot,
    string Model, EntrySource Source, Guid[] ReferenceRoundIds, string[] Tags, string Notes,
    RoundState Status, int Version, DateTime CreatedUtc, ResultDto[] Results)
{
    public string LatestOutput => Results.LastOrDefault()?.Output ?? "";
    public string Label => $"#{Number:00} · {EmployeeSnapshot.Name}";
}
public sealed record PageDto<T>(T[] Items, int Total, int Skip, int Take);
public sealed record ActivityDto(Guid Id, Guid ProjectId, Guid ChainId, string ChainName, Guid EmployeeId,
    int Number, string InputPreview, string OutputPreview, RoundState Status, int Version, DateTime CreatedUtc,
    Guid? ParentRoundId, Guid? RevisesRoundId);
public sealed record RoundSaved(RoundDto Round, bool Replayed);
public sealed record ContextDto(ProjectDto Project, ChainDto Chain, EmployeeDto Employee,
    RoundDto[] Ancestors, RoundDto[] References, string CompiledContext);
public sealed record GraphNode(Guid Id, Guid? ParentRoundId, int Number, Guid EmployeeId, string EmployeeName,
    string Role, RoundState Status, string Preview, Guid[] ReferenceRoundIds, Guid? RevisesRoundId);
public sealed record GraphDto(Guid ProjectId, Guid ChainId, GraphNode[] Nodes);
public sealed record ComparisonDto(RoundDto Left, RoundDto Right, string InputDiff, string OutputDiff,
    int? LeftOutputVersion = null, int? RightOutputVersion = null);
public sealed record AccessKeyDto(Guid Id, string Name, Guid[] ProjectIds, DateTime CreatedUtc, DateTime? RevokedUtc);
public sealed record IssuedKey(string Token, AccessKeyDto Key);
public sealed record RoleTemplate(string Id, string Name, string Role, string Responsibilities, string BasePrompt, string[] Skills);

public sealed record ProjectInput
{
    public string Name { get; init; } = "";
    public string Goal { get; init; } = "";
    public string Background { get; init; } = "";
    public string Constraints { get; init; } = "";
    public ProjectState Status { get; init; }
    public int? ExpectedVersion { get; init; }
}
public sealed record EmployeeInput
{
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public string Responsibilities { get; init; } = "";
    public string BasePrompt { get; init; } = "";
    public string[] Skills { get; init; } = [];
    public bool IsActive { get; init; } = true;
    public int? ExpectedVersion { get; init; }
}
public sealed record ChainInput
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? CreateId { get; init; }
    public string Name { get; init; } = "";
    public string Goal { get; init; } = "";
    public string AcceptanceCriteria { get; init; } = "";
    public ChainState Status { get; init; }
    public int? ExpectedVersion { get; init; }
}
public sealed record RoundInput
{
    public Guid ClientRequestId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid? ParentRoundId { get; init; }
    public Guid? RevisesRoundId { get; init; }
    public string Input { get; init; } = "";
    public string Model { get; init; } = "";
    public EntrySource Source { get; init; }
    public Guid[] ReferenceRoundIds { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string Notes { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExpectedEmployeeVersion { get; init; }
}
public sealed record ResultInput
{
    public Guid ClientRequestId { get; init; }
    public int ExpectedVersion { get; init; }
    public string Output { get; init; } = "";
    public RoundState Status { get; init; } = RoundState.Completed;
    public string[] Artifacts { get; init; } = [];
    public string Note { get; init; } = "";
}
public sealed record AccessKeyInput
{
    public string Name { get; init; } = "";
    public Guid[] ProjectIds { get; init; } = [];
}
public sealed record SearchQuery(string? Text = null, Guid? EmployeeId = null, Guid? ChainId = null,
    string? Tag = null, RoundState? Status = null, int Skip = 0, int Take = 50);

public sealed class CrewException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
public static class Rules
{
    public static string Text(string? value, string name, int maximum, bool required = false, bool trim = false)
    {
        value ??= "";
        if (value.Length > maximum || required && string.IsNullOrWhiteSpace(value))
            throw new CrewException(400, "validation_failed", $"{name}须{(required ? "有内容并且" : "")}不超过 {maximum} 个字符。");
        return trim ? value.Trim() : value;
    }
    public static string[] Items(string[]? values, string name, int count = 24, int length = 120)
    {
        values ??= [];
        if (values.Length > count) throw new CrewException(400, "validation_failed", $"{name}最多 {count} 项。");
        return values.Select(v => Text(v, name, length, true, true)).Distinct(StringComparer.Ordinal).ToArray();
    }
    public static void EnumValue<T>(T value) where T : struct, Enum
    { if (!Enum.IsDefined(value)) throw new CrewException(400, "validation_failed", "状态值不受支持。"); }
    public static void MatchVersion(int actual, int? expected)
    {
        if (expected is null or < 1) throw new CrewException(400, "version_required", "更新时请提供 expectedVersion。");
        if (actual != expected) throw new CrewException(409, "version_conflict", "记录已被其他操作更新。请刷新后重试。");
    }
    public static void RequestId(Guid id)
    { if (id == Guid.Empty) throw new CrewException(400, "request_id_required", "请提供非空的 clientRequestId，并在重试时保留它。"); }
}
public static class RoleTemplates
{
    public static IReadOnlyList<RoleTemplate> All { get; } = [
        new("gameplay", "玩法设计师", "玩法设计", "定义核心循环、规则、关卡和交互，明确验收标准。",
            "你负责本项目的玩法设计。结合项目目标和约束，给出可执行的机制、边界条件和验收标准。标注假设与尚待确认的问题。", ["game-design"]),
        new("programming", "程序员", "程序开发", "根据批准的设计实现功能、分析问题并维护代码。",
            "你负责本项目的程序开发。先阅读关联设计和现有代码，再实现当前任务。说明修改、验证结果与剩余限制。", ["implementation", "debugging"]),
        new("art", "美术设计师", "视觉设计", "维护视觉规范、素材需求和图像生成提示词。",
            "你负责本项目的美术设计。保持视觉风格一致，给出素材清单、规格与可复用的生成提示词。说明构图、色彩、尺寸和使用场景。", ["art-direction"]),
        new("balance", "数值策划", "数值平衡", "设计属性、成长曲线、经济系统并评估平衡。",
            "你负责本项目的数值平衡。明确单位、公式、参数范围、假设与测试场景，提供可核对的计算过程和调整建议。", ["balancing"])
    ];
}
