using System.Collections.ObjectModel;
using HabitTracker.ViewModels;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed class ActivityCard(ActivityDto record, int position, bool isLast)
{
    public ActivityDto Record { get; } = record;
    public Guid Id => Record.Id;
    public string Title => Compact(Record.InputPreview, 40);
    public string Summary => Compact(string.IsNullOrWhiteSpace(Record.OutputPreview) ? Record.InputPreview : Record.OutputPreview, 130);
    public string SummaryLabel => string.IsNullOrWhiteSpace(Record.OutputPreview) ? "本轮提示词" : "成果摘要";
    public string Time => Record.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm");
    public string ChainName => Record.ChainName;
    public string State => Labels.State(Record.Status);
    public string StateBackground => Record.Status switch { RoundState.Completed => "#E7F0DB", RoundState.Failed => "#FCE5D8", _ => "#FFF0D0" };
    public string Marker => position.ToString("00");
    public string Revision => Record.RevisesRoundId.HasValue ? "输入修订" : Record.Version > 2 ? $"已修正 · v{Record.Version}" : "";
    public bool HasRevision => Revision.Length > 0;
    public bool IsLast { get; } = isLast;
    public string AccessibleName => $"{Title}，{ChainName}，{State}";
    private static string Compact(string value, int maximum)
    {
        var text = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= maximum) return text;
        if (char.IsHighSurrogate(text[maximum - 1])) maximum--;
        return text[..maximum] + "…";
    }
}

public sealed class EmployeeTimeline(EmployeeDto employee, IEnumerable<ActivityDto> records, int? total = null) : ViewModelBase
{
    private bool _selected;
    public EmployeeDto Employee { get; } = employee;
    public Guid Id => Employee.Id;
    public string Name => Employee.Name;
    public string Role => Employee.Role;
    public string Initial => Name.Length > 0 ? Name[..1] : "人";
    public ObservableCollection<ActivityCard> Cards { get; } = MakeCards(records);
    public int Total { get; } = total ?? records.Count();
    public bool HasMore => Cards.Count < Total;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
    public bool IsEmpty => Cards.Count == 0;
    public string CountLabel => HasMore ? $"{Total} 轮记录 · 当前显示 {Cards.Count} 轮" : $"{Total} 轮工作记录";
    private static ObservableCollection<ActivityCard> MakeCards(IEnumerable<ActivityDto> records)
    {
        var ordered = records.OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).ToArray();
        return new(ordered.Select((record, index) => new ActivityCard(record, index + 1, index == ordered.Length - 1)));
    }
}
