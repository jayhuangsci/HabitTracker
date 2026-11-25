using System.Collections.ObjectModel;
using HabitTracker.ViewModels;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
public sealed class RoundNode(GraphNode node)
{
    public GraphNode Node { get; } = node;
    public Guid Id => Node.Id;
    public string Label => $"#{Node.Number:00}  {Node.EmployeeName}";
    public string Detail => $"{Node.Role} · {Labels.State(Node.Status)}";
    public string Preview => Node.Preview.Replace("\r", " ").Replace("\n", " ");
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<RoundNode> Children { get; } = [];
}
public static class Labels
{
    public static string State(RoundState value) => value switch { RoundState.PendingOutput => "待输出", RoundState.Completed => "已完成", _ => "失败" };
    public static Choice<ProjectState>[] ProjectStates { get; } = [new(ProjectState.Active, "进行中"), new(ProjectState.Paused, "暂停"), new(ProjectState.Archived, "已归档")];
    public static Choice<ChainState>[] ChainStates { get; } = [new(ChainState.Active, "进行中"), new(ChainState.Completed, "已完成"), new(ChainState.Archived, "已归档")];
    public static Choice<RoundState>[] ResultStates { get; } = [new(RoundState.Completed, "已完成"), new(RoundState.Failed, "失败")];
}
