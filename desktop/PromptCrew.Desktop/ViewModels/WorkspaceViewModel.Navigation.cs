using System.Collections.ObjectModel;
using HabitTracker.ViewModels;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private bool _overview = true;
    private EmployeeDto? _activeEmployee;
    private ActivityDto[] _activities = [];
    private string _contextHeading = "当前分支上下文";
    public string ContextHeading { get => _contextHeading; private set => SetProperty(ref _contextHeading, value); }
    private readonly Dictionary<Guid, Guid> _employeeChains = [];
    public event Action? OverviewShown;
    public event Action? LeavingOverview;
    public event Action? EmployeeOpened;
    public event Action? RecordOpened;
    public ObservableCollection<EmployeeTimeline> EmployeeTimelines { get; } = [];
    public bool IsOverview => _overview;
    public bool IsEmployeeWorkspace => !_overview;
    public EmployeeDto? ActiveEmployee => _activeEmployee;
    public EmployeeTimeline? ActiveTimeline => EmployeeTimelines.FirstOrDefault(x => x.Id == _activeEmployee?.Id);
    public string WorkspaceTitle => _activeEmployee?.Name ?? "员工工作区";
    public string WorkspaceRole => _activeEmployee == null ? "" : $"{_activeEmployee.Role} · {(ActiveTimeline?.Cards.Count ?? 0)} 轮工作记录";
    public string ActiveResponsibilities => _activeEmployee?.Responsibilities ?? "";
    public string ActiveBasePrompt => _activeEmployee?.BasePrompt ?? "";
    public string ActiveSkills => string.Join(" / ", _activeEmployee?.Skills ?? []);
    public string OverviewTitle => _project?.Name ?? "项目总览";
    public string OverviewSummary => $"{Employees.Count} 名员工 · {Chains.Count} 条任务链 · {EmployeeTimelines.Sum(x => x.Total)} 轮工作记录";
    public bool HasTeam => EmployeeTimelines.Count > 0;
    public bool HasNoTeam => !HasTeam;
    public bool HasNoProject => !HasProject;
    public ChainDto[] ParticipatingChains => Chains.Where(c => _activities.Any(r => r.EmployeeId == _activeEmployee?.Id && r.ChainId == c.Id)).ToArray();
    public bool HasParticipatingChains => ParticipatingChains.Length > 0;
    public string RecordRelations => _round == null ? "" : $"所属任务：{Chains.FirstOrDefault(x => x.Id == _round.ChainId)?.Name}\n父节点：{(_round.ParentRoundId?.ToString() ?? "首轮")}\n输入修订自：{(_round.RevisesRoundId?.ToString() ?? "无")}\n引用轮次：{( _round.ReferenceRoundIds.Length == 0 ? "无" : string.Join(", ", _round.ReferenceRoundIds))}";
    public RelayCommand OverviewCommand { get; private set; } = null!;
    public RelayCommand ResumeWorkCommand { get; private set; } = null!;
    public RelayCommand OpenEmployeeCommand { get; private set; } = null!;
    public RelayCommand OpenActivityCommand { get; private set; } = null!;
    public AsyncRelayCommand RecordContextCommand { get; private set; } = null!;
    private void InitializeNavigation()
    {
        LoadOlderCommand = new(value => { if (value is Guid id) _ = LoadOlder(id); }, () => !IsBusy);
        OverviewCommand = new(_ => GoOverview(), () => !IsBusy);
        ResumeWorkCommand = new(_ => { SetOverview(false); EmployeeOpened?.Invoke(); }, () => !IsBusy && _activeEmployee != null);
        OpenEmployeeCommand = new(value => { if (value is Guid id) _ = OpenEmployeeAsync(id); }, () => CanSelectWorkspace);
        OpenActivityCommand = new(value => { if (value is Guid id) _ = OpenActivityAsync(id); }, () => CanSelectWorkspace);
        RecordContextCommand = new(() => RunAsync(LoadRecordContext), () => !IsBusy && _round != null);
    }
    public void GoOverview()
    {
        Persist(); SetOverview(true); OverviewShown?.Invoke();
    }
    private void SetOverview(bool value)
    { if (_overview && !value) LeavingOverview?.Invoke(); _overview = value; NotifyNavigation(); }
    private void SetActiveEmployee(EmployeeDto? employee)
    {
        _activeEmployee = employee;
        _settings.EmployeeId = employee?.Id;
        foreach (var lane in EmployeeTimelines) lane.IsSelected = !_overview && lane.Id == employee?.Id;
        NotifyNavigation();
    }
    private void NotifyNavigation()
    {
        foreach (var lane in EmployeeTimelines) lane.IsSelected = !_overview && lane.Id == _activeEmployee?.Id;
        foreach (var property in new[] { nameof(IsOverview), nameof(IsEmployeeWorkspace), nameof(ActiveEmployee), nameof(ActiveTimeline), nameof(WorkspaceTitle), nameof(WorkspaceRole),
            nameof(ActiveResponsibilities), nameof(ActiveBasePrompt), nameof(ActiveSkills), nameof(OverviewTitle), nameof(OverviewSummary), nameof(HasTeam), nameof(HasNoTeam), nameof(HasNoProject),
            nameof(ParticipatingChains), nameof(HasParticipatingChains), nameof(RecordRelations) }) OnPropertyChanged(property);
        OverviewCommand?.RaiseCanExecuteChanged(); ResumeWorkCommand?.RaiseCanExecuteChanged(); RecordContextCommand?.RaiseCanExecuteChanged();
        LoadOlderCommand?.RaiseCanExecuteChanged(); OpenEmployeeCommand?.RaiseCanExecuteChanged(); OpenActivityCommand?.RaiseCanExecuteChanged();
    }
    public RelayCommand LoadOlderCommand { get; private set; } = null!;
    private async Task LoadActivity()
    {
        var project = _project;
        if (project == null) { _activities = []; EmployeeTimelines.Clear(); NotifyNavigation(); return; }
        using var limit = new SemaphoreSlim(3);
        var token = OperationToken;
        var lanes = await Task.WhenAll(Employees.OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).Select(async employee =>
        {
            await limit.WaitAsync(token);
            try
            {
                var page = await _api.ActivityPage(project.Id, employee.Id, ct: token);
                token.ThrowIfCancellationRequested();
                return new EmployeeTimeline(employee, page.Items, page.Total);
            }
            finally { limit.Release(); }
        }));
        token.ThrowIfCancellationRequested();
        Replace(EmployeeTimelines, lanes);
        _activities = lanes.SelectMany(x => x.Cards).Select(x => x.Record).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).ToArray();
        NotifyNavigation();
    }
    private Task LoadOlder(Guid employeeId) => RunAsync(async () =>
    {
        var lane = EmployeeTimelines.FirstOrDefault(x => x.Id == employeeId);
        if (_project == null || lane == null || !lane.HasMore) return;
        var page = await Read(ct => _api.ActivityPage(_project.Id, employeeId, lane.Cards.Count, 50, ct));
        var records = lane.Cards.Select(x => x.Record).Concat(page.Items).DistinctBy(x => x.Id).ToArray();
        EmployeeTimelines[EmployeeTimelines.IndexOf(lane)] = new(lane.Employee, records, page.Total);
        _activities = EmployeeTimelines.SelectMany(x => x.Cards).Select(x => x.Record).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).ToArray();
        NotifyNavigation(); Status = "已加载更早的工作记录。";
    });
    public Task OpenEmployeeAsync(Guid id) => NavigateAsync(async () =>
    {
        var employee = Employees.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("员工不属于当前项目。");
        if (HasPending && employee.Id != _employee?.Id) throw new InvalidOperationException("请先确认待保存的请求，再切换员工。");
        if (_activeEmployee?.Id != id)
        {
            Persist(); SetActiveEmployee(employee);
            var preferred = _employeeChains.GetValueOrDefault(id);
            if (preferred == Guid.Empty) preferred = _activities.LastOrDefault(x => x.EmployeeId == id)?.ChainId ?? _chain?.Id ?? Guid.Empty;
            _loading = true;
            try { SelectedChain = Chains.FirstOrDefault(x => x.Id == preferred) ?? Chains.FirstOrDefault(); }
            finally { _loading = false; }
            await LoadChain();
        }
        SetOverview(false); EmployeeOpened?.Invoke();
    });
    public Task OpenActivityAsync(Guid id) => NavigateAsync(async () =>
    {
        if (_project == null) return;
        var row = await Read(ct => _api.Round(_project.Id, id, ct));
        if (HasPending && (_employee?.Id != row.EmployeeId || _chain?.Id != row.ChainId))
            throw new InvalidOperationException("请先确认待保存请求，再打开其他员工或任务链。");
        if (_activeEmployee?.Id != row.EmployeeId || _chain?.Id != row.ChainId)
        {
            Persist(); SetActiveEmployee(Employees.FirstOrDefault(x => x.Id == row.EmployeeId));
            _loading = true;
            try { SelectedChain = Chains.FirstOrDefault(x => x.Id == row.ChainId); }
            finally { _loading = false; }
            await LoadChain();
        }
        await SelectRoundCore(row.Id); SetOverview(false); RecordOpened?.Invoke();
    });
    public Task ViewRecordContextAsync() => RunAsync(LoadRecordContext);
    private async Task LoadRecordContext()
    {
        if (_project == null || _round == null) return;
        var context = await Read(ct => _api.Context(_project.Id, _round.ChainId, _round.EmployeeId, _round.Id, ct: ct));
        CompiledContext = context.CompiledContext;
        ContextHeading = "本轮上下文 · " + _round.Label;
        Status = $"此轮上下文：{context.Ancestors.Length} 条祖先及当前记录，{context.References.Length} 个显式引用。";
    }
}
