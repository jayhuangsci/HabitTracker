using PromptCrew.Core;

namespace PromptCrew.Api.Data;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
public sealed class CrewProject : Entity
{
    public string Name { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Background { get; set; } = "";
    public string Constraints { get; set; } = "";
    public ProjectState Status { get; set; }
}
public sealed class Employee : Entity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Responsibilities { get; set; } = "";
    public string BasePrompt { get; set; } = "";
    public string SkillsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
public sealed class TaskChain : Entity
{
    public string? CreateFingerprint { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Goal { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public ChainState Status { get; set; }
    public int NextNumber { get; set; } = 1;
}
public sealed class PromptRound : Entity
{
    public Guid ProjectId { get; set; }
    public Guid ChainId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? ParentRoundId { get; set; }
    public Guid? RevisesRoundId { get; set; }
    public int Number { get; set; }
    public string RequestHash { get; set; } = "";
    public string Input { get; set; } = "";
    public string EmployeeSnapshotJson { get; set; } = "";
    public string Model { get; set; } = "";
    public EntrySource Source { get; set; }
    public string Notes { get; set; } = "";
    public RoundState Status { get; set; }
    public List<RoundResult> Results { get; set; } = [];
    public List<RoundReference> References { get; set; } = [];
    public List<RoundTag> Tags { get; set; } = [];
}
public sealed class RoundResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid RoundId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public int Version { get; set; }
    public string Output { get; set; } = "";
    public RoundState Status { get; set; }
    public string ArtifactsJson { get; set; } = "[]";
    public string Note { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
public sealed class RoundReference
{
    public Guid ProjectId { get; set; }
    public Guid RoundId { get; set; }
    public Guid ReferencedRoundId { get; set; }
}
public sealed class RoundTag
{
    public Guid RoundId { get; set; }
    public string Value { get; set; } = "";
}
public sealed class AccessKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string ProjectsJson { get; set; } = "[]";
    public DateTime CreatedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
}
