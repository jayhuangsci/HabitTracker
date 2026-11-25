using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using HabitTracker.ViewModels;
using PromptCrew.Client;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public enum CatalogSection { Project, Employee, Chain }

public sealed partial class WorkspaceViewModel : ViewModelBase, IDisposable
{
    private CrewClient _api;
    private readonly DesktopSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private EditorDraft _draft = new();
    private ProjectDto? _project;
    private ChainDto? _chain;
    private EmployeeDto? _employee;
    private RoundDto? _round;
    private ResultDto? _resultVersion;
    private RoundDto? _compareTarget;
    private bool _busy, _error, _loading, _admin, _disposed;
    private string _status = "正在连接工作台…", _context = "", _inputDiff = "", _outputDiff = "";
    private string _searchText = "", _searchTag = "";
    private EmployeeDto? _searchEmployee;
    private Choice<RoundState?>? _searchState;
    private GraphDto? _graph;
    public event Action<CatalogSection, bool>? EditRequested;
    public event Action? ConnectionRequested;
    public event Action? KeysRequested;

    public WorkspaceViewModel(DesktopSettings settings, CrewClient? api = null)
    {
        _settings = settings;
        _api = api ?? Connect();
        _status = settings.Warning.Length > 0 ? settings.Warning : _status;
        RefreshCommand = new(() => RunAsync(RefreshAll), () => !IsBusy);
        SaveRoundCommand = new(() => RunAsync(SaveRound), () => CanSaveRound);
        SaveResultCommand = new(() => RunAsync(SaveResult), () => CanSaveResult);
        ResolvePendingCommand = new(() => RunAsync(ResolvePending), () => !IsBusy && HasPending);
        ContinueCommand = new(_ => Continue(false), () => !IsBusy && _round != null && !HasPending);
        ReviseCommand = new(_ => Continue(true), () => !IsBusy && _round != null && !HasPending && (_activeEmployee == null || _round.EmployeeId == _activeEmployee.Id));
        RootCommand = new(_ => { _draft.ParentId = null; _draft.RevisesId = null; NotifyDraft(); ScheduleSave(); }, () => CanEditDraft);
        ContextCommand = new(() => RunAsync(LoadContext), () => !IsBusy && _chain != null && _employee != null);
        SearchCommand = new(() => RunAsync(Search), () => !IsBusy && _project != null);
        CompareCommand = new(() => RunAsync(Compare), () => !IsBusy && _round != null && _compareTarget != null);
        NewProjectCommand = new(_ => EditRequested?.Invoke(CatalogSection.Project, false), () => !IsBusy && IsAdmin && !HasPending);
        EditProjectCommand = new(_ => EditRequested?.Invoke(CatalogSection.Project, true), () => !IsBusy && _project != null && !HasPending);
        NewEmployeeCommand = new(_ => EditRequested?.Invoke(CatalogSection.Employee, false), () => CanEditCatalog);
        EditEmployeeCommand = new(_ => EditRequested?.Invoke(CatalogSection.Employee, true), () => CanEditCatalog && _employee != null);
        NewChainCommand = new(_ => EditRequested?.Invoke(CatalogSection.Chain, false), () => CanEditCatalog);
        EditChainCommand = new(_ => EditRequested?.Invoke(CatalogSection.Chain, true), () => CanEditCatalog && _chain != null);
        ConnectionCommand = new(_ => ConnectionRequested?.Invoke(), () => !IsBusy);
        KeysCommand = new(_ => KeysRequested?.Invoke(), () => IsAdmin && !IsBusy);
        InitializeNavigation();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Persist(); };
        _searchState = SearchStates[0];
    }
    private CrewClient Connect() => new(_settings.ApiUrl, () => CrewClient.ReadKeyFile(_settings.KeyFile));
    public CrewClient Api => _api;
    public DesktopSettings Settings => _settings;
    public ObservableCollection<ProjectDto> Projects { get; } = [];
    public ObservableCollection<EmployeeDto> Employees { get; } = [];
    public ObservableCollection<ChainDto> Chains { get; } = [];
    public ObservableCollection<RoundNode> Roots { get; } = [];
    public ObservableCollection<RoundDto> SearchResults { get; } = [];
    public ObservableCollection<RoundDto> ComparisonOptions { get; } = [];
    public ObservableCollection<RoleTemplate> Templates { get; } = [];
    public Choice<RoundState?>[] SearchStates { get; } = [new(null, "全部状态"), new(RoundState.PendingOutput, "待输出"), new(RoundState.Completed, "已完成"), new(RoundState.Failed, "失败")];
    public Choice<RoundState>[] ResultStates => Labels.ResultStates;
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyState(); } }
    public bool IsAdmin { get => _admin; private set { if (SetProperty(ref _admin, value)) NotifyState(); } }
    public bool HasError { get => _error; private set => SetProperty(ref _error, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasProject => _project != null;
    public bool HasChain => _chain != null;
    public bool HasRound => _round != null;
    public bool IsIdle => !IsBusy;
    public bool HasPending => _draft.PendingRound != null || _draft.PendingResult != null;
    public bool CanSelectWorkspace => !IsBusy && !HasPending;
    public bool CanEditDraft => !IsBusy && !HasPending && _chain != null;
    public bool CanEditCatalog => !IsBusy && _project != null && !HasPending;
    public bool CanSaveRound => !IsBusy && _project != null && _chain != null && _employee != null
        && (_draft.PendingRound != null || !HasPending && !string.IsNullOrWhiteSpace(DraftInput));
    public bool CanSaveResult => !IsBusy && _project != null && _round != null
        && (_draft.PendingResult != null || !HasPending && (ResultStatus == RoundState.Failed && !string.IsNullOrWhiteSpace(ResultNote) || !string.IsNullOrWhiteSpace(DraftOutput)));
    public string SaveRoundLabel => _draft.PendingRound != null ? "重试同一输入" : "保存输入  →";
    public string SaveResultLabel => _draft.PendingResult != null ? "重试同一结果" : "追加结果版本";
    public string ProjectSummary => _project == null ? "创建项目，开始组织你的 AI 团队。" : _project.Goal;
    public string ProjectContext => _project == null ? "" : $"目标\n{_project.Goal}\n\n背景\n{_project.Background}\n\n约束\n{_project.Constraints}";
    public string EmployeeDescription => _employee == null ? "选择一名员工，查看其职责与基础提示词。" : $"{_employee.Role} · 定义 v{_employee.Version}\n\n{_employee.Responsibilities}\n\n基础提示词\n{_employee.BasePrompt}\n\nSkills · {string.Join(" / ", _employee.Skills)}";
    public string ChainSummary => _chain == null ? "选择或创建任务链" : $"{_chain.Goal}\n完成标准：{_chain.AcceptanceCriteria}";
    public string CountLabel => $"{Employees.Count} 名员工 · {Chains.Count} 条任务链 · {_graph?.Nodes.Length ?? 0} 轮记录";
    public string ParentLabel => _draft.ParentId.HasValue ? "继续自 " + (_graph?.Nodes.FirstOrDefault(x => x.Id == _draft.ParentId) is { } n ? $"#{n.Number:00} {n.EmployeeName}" : _draft.ParentId.ToString()) : "首轮 · 新分支根节点";
    public string CurrentEmployeeLabel => _employee == null ? "请选择负责员工" : $"本轮员工：{_employee.Name} · {_employee.Role}";
    public string RevisionLabel => _draft.RevisesId.HasValue ? "这是一个保留原记录的输入修订版本。" : "保存后原始输入保持不变，可继续分支或创建修订。";
    public string RecordTitle => _round == null ? "选择一轮记录" : $"{_round.Label} · {Labels.State(_round.Status)} · v{_round.Version}";
    public string RecordMetadata => _round == null ? "" : $"记录 ID  {_round.Id}\n请求 ID  {_round.ClientRequestId}\n{_round.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {_round.Model} · {(_round.Source == EntrySource.Agent ? "Agent 保存" : "手动录入")}\n角色快照 v{_round.EmployeeSnapshot.DefinitionVersion} · {_round.EmployeeSnapshot.Role}\n{_round.EmployeeSnapshot.Responsibilities}\n标签：{string.Join(" / ", _round.Tags)}\n引用：{string.Join(", ", _round.ReferenceRoundIds)}\n备注：{_round.Notes}";
    public string RecordInput => _round?.Input ?? "";
    public string RecordBasePrompt => _round?.EmployeeSnapshot.BasePrompt ?? "";
    public string HistoricalOutput => _resultVersion?.Output ?? "此轮尚未补充输出。";
    public string HistoricalNote => _resultVersion == null ? "" : $"v{_resultVersion.Version} · {Labels.State(_resultVersion.Status)} · {_resultVersion.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n{_resultVersion.Note}\n成果：{string.Join("\n", _resultVersion.Artifacts)}";
    public string CompiledContext { get => _context; private set => SetProperty(ref _context, value); }
    public string InputDiff { get => _inputDiff; private set => SetProperty(ref _inputDiff, value); }
    public string OutputDiff { get => _outputDiff; private set => SetProperty(ref _outputDiff, value); }
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public string SearchTag { get => _searchTag; set => SetProperty(ref _searchTag, value); }
    public EmployeeDto? SearchEmployee { get => _searchEmployee; set => SetProperty(ref _searchEmployee, value); }
    public Choice<RoundState?>? SearchState { get => _searchState; set => SetProperty(ref _searchState, value); }
    public ProjectDto? SelectedProject
    {
        get => _project;
        set { if (!SetProperty(ref _project, value)) return; NotifyState(); if (!_loading) _ = RunAsync(LoadProject); }
    }
    public ChainDto? SelectedChain
    {
        get => _chain;
        set { if (!SetProperty(ref _chain, value)) return; NotifyState(); if (!_loading) _ = RunAsync(LoadChain); }
    }
    public EmployeeDto? SelectedEmployee
    {
        get => _employee;
        set { if (!SetProperty(ref _employee, value)) return; if (!_loading) { _draft.EmployeeId = value?.Id; ScheduleSave(); } NotifyState(); }
    }
    public RoundDto? SelectedRound => _round;
    public ResultDto? SelectedResultVersion { get => _resultVersion; set { if (SetProperty(ref _resultVersion, value)) { OnPropertyChanged(nameof(HistoricalOutput)); OnPropertyChanged(nameof(HistoricalNote)); } } }
    public RoundDto? ComparisonTarget
    {
        get => _compareTarget;
        set { if (SetProperty(ref _compareTarget, value)) { LeftOutputVersion = 0; OnPropertyChanged(nameof(LeftOutputVersions)); NotifyState(); } }
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveRoundCommand { get; }
    public AsyncRelayCommand SaveResultCommand { get; }
    public AsyncRelayCommand ResolvePendingCommand { get; }
    public AsyncRelayCommand ContextCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand CompareCommand { get; }
    public RelayCommand ContinueCommand { get; }
    public RelayCommand ReviseCommand { get; }
    public RelayCommand RootCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand EditProjectCommand { get; }
    public RelayCommand NewEmployeeCommand { get; }
    public RelayCommand EditEmployeeCommand { get; }
    public RelayCommand NewChainCommand { get; }
    public RelayCommand EditChainCommand { get; }
    public RelayCommand ConnectionCommand { get; }
    public RelayCommand KeysCommand { get; }

    public Task InitializeAsync() => RunAsync(RefreshAll);
    public async Task ChangeConnection(string url, string keyFile)
    {
        _ = CrewClient.Address(url);
        if (HasPending && !string.Equals(_settings.ApiUrl.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("先确认待保存的请求，再切换到其他服务。");
        var candidate = new CrewClient(url, () => CrewClient.ReadKeyFile(keyFile));
        try { await candidate.Session(); }
        catch { candidate.Dispose(); throw; }
        _api.Dispose(); _api = candidate; _settings.ApiUrl = url; _settings.KeyFile = keyFile;
        await RunAsync(RefreshAll);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; HasError = false; Status = "正在更新工作台…";
        try { await action(); if (Status == "正在更新工作台…") Status = "工作台已更新。"; }
        catch (Exception e) when (e is CrewApiException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { HasError = true; Status = e is HttpRequestException or TaskCanceledException ? "无法连接服务。请检查连接设置；草稿和待确认请求会保留。" : e.Message; }
        finally { IsBusy = false; }
    }
    private async Task RefreshAll()
    {
        var session = await _api.Session(); IsAdmin = session.IsAdmin;
        Replace(Templates, await _api.Templates());
        var old = _project?.Id ?? _settings.ProjectId;
        _loading = true;
        try { Replace(Projects, await _api.Projects()); SelectedProject = Projects.FirstOrDefault(x => x.Id == old) ?? Projects.FirstOrDefault(); }
        finally { _loading = false; }
        await LoadProject(); Persist();
        Status = "已连接 · " + session.Name + " · 草稿保存在此设备";
    }
    private async Task LoadProject()
    {
        var previousEmployee = _activeEmployee;
        if (previousEmployee != null && previousEmployee.ProjectId != _project?.Id) { _activeEmployee = null; _employeeChains.Clear(); SetOverview(true); }
        _settings.ProjectId = _project?.Id;
        _loading = true;
        try
        {
            Replace(Employees, _project == null ? [] : await _api.Employees(_project.Id));
            Replace(Chains, _project == null ? [] : await _api.Chains(_project.Id));
            SelectedChain = Chains.FirstOrDefault(x => x.Id == _settings.ChainId) ?? Chains.FirstOrDefault();
            var employeeId = _activeEmployee?.Id ?? _settings.EmployeeId;
            if (_chain != null && _settings.Drafts.TryGetValue(_settings.ApiUrl.TrimEnd('/').ToLowerInvariant() + "|" + _chain.Id, out var legacy)
                && (!Employees.Any(x => x.Id == employeeId) || legacy.PendingRound != null || legacy.PendingResult != null)) employeeId = legacy.EmployeeId;
            SetActiveEmployee(Employees.FirstOrDefault(x => x.Id == employeeId) ?? Employees.OrderBy(x => x.CreatedUtc).FirstOrDefault());
        }
        finally { _loading = false; }
        SearchResults.Clear(); _searchEmployee = null; OnPropertyChanged(nameof(SearchEmployee));
        await LoadActivity();
        await LoadChain();
    }
    private async Task LoadChain()
    {
        _settings.ChainId = _chain?.Id;
        if (_chain != null)
        {
            var legacyKey = _settings.ApiUrl.TrimEnd('/').ToLowerInvariant() + "|" + _chain.Id;
            var key = legacyKey + "|" + _activeEmployee?.Id;
            if (!_settings.Drafts.TryGetValue(key, out var draft))
            {
                if (_settings.Drafts.TryGetValue(legacyKey, out var legacy) && (legacy.EmployeeId == _activeEmployee?.Id || legacy.EmployeeId == null))
                { draft = legacy; _settings.Drafts.Remove(legacyKey); }
                else draft = new();
                _settings.Drafts[key] = draft;
            }
            _draft = draft;
            if (_activeEmployee != null) _employeeChains[_activeEmployee.Id] = _chain.Id;
        }
        else _draft = new();
        _loading = true;
        try { SelectedEmployee = _activeEmployee ?? Employees.FirstOrDefault(x => x.Id == _draft.EmployeeId) ?? Employees.FirstOrDefault(x => x.IsActive) ?? Employees.FirstOrDefault(); }
        finally { _loading = false; }
        _draft.EmployeeId = _employee?.Id;
        _round = null; OnPropertyChanged(nameof(SelectedRound));
        SelectedResultVersion = null;
        if (_project != null && _chain != null)
        {
            _graph = await _api.Graph(_project.Id, _chain.Id); BuildTree();
            if (_draft.ResultRoundId.HasValue && _graph.Nodes.Any(x => x.Id == _draft.ResultRoundId && x.EmployeeId == _employee?.Id))
                await SelectRoundCore(_draft.ResultRoundId.Value);
            else if (_graph.Nodes.LastOrDefault(x => x.EmployeeId == _employee?.Id) is { } last) await SelectRoundCore(last.Id);
        }
        else { _graph = null; Roots.Clear(); }
        ComparisonOptions.Clear(); ComparisonTarget = null; CompiledContext = ""; ContextHeading = "当前分支上下文"; NotifyDraft(); NotifyState(); Persist();
    }
    private async Task ReloadGraph(Guid? select = null)
    {
        if (_project == null || _chain == null) return;
        _graph = await _api.Graph(_project.Id, _chain.Id); BuildTree();
        var chainId = _chain.Id;
        _loading = true;
        try { Replace(Chains, await _api.Chains(_project.Id)); SelectedChain = Chains.FirstOrDefault(x => x.Id == chainId); }
        finally { _loading = false; }
        if (select.HasValue) await SelectRoundCore(select.Value);
        await LoadActivity();
        NotifyState();
    }
    private void BuildTree()
    {
        Roots.Clear();
        if (_graph == null) return;
        var nodes = _graph.Nodes.ToDictionary(x => x.Id, x => new RoundNode(x));
        foreach (var item in _graph.Nodes)
        {
            if (item.ParentRoundId.HasValue && nodes.TryGetValue(item.ParentRoundId.Value, out var parent)) parent.Children.Add(nodes[item.Id]);
            else Roots.Add(nodes[item.Id]);
        }
    }
    public Task SelectRoundAsync(Guid id) => RunAsync(() => SelectRoundCore(id));
    private async Task SelectRoundCore(Guid id)
    {
        if (_project == null) return;
        var row = await _api.Round(_project.Id, id); _round = row;
        RightOutputVersion = 0; OnPropertyChanged(nameof(RightOutputVersions));
        void SelectNode(IEnumerable<RoundNode> nodes)
        { foreach (var node in nodes) { node.IsSelected = node.Id == id; SelectNode(node.Children); } }
        SelectNode(Roots);
        if (_draft.ResultRoundId != id && !HasPending)
        {
            if (_draft.ResultRoundId is { } previous)
                _draft.Outputs[previous] = new(_draft.Output, _draft.Artifacts, _draft.ResultNote, _draft.ResultStatus);
            var draft = _draft.Outputs.GetValueOrDefault(id);
            _draft.Output = draft?.Output ?? row.LatestOutput;
            _draft.Artifacts = draft?.Artifacts ?? string.Join("\n", row.Results.LastOrDefault()?.Artifacts ?? []);
            _draft.ResultNote = draft?.Note ?? ""; _draft.ResultStatus = draft?.Status ?? RoundState.Completed; _draft.ResultRoundId = id;
        }
        SelectedResultVersion = row.Results.LastOrDefault();
        OnPropertyChanged(nameof(SelectedRound)); NotifyDraft(); NotifyState();
        ScheduleSave();
    }
    public async Task CatalogSaved(CatalogSection section, Guid id)
    {
        if (section == CatalogSection.Project) { _settings.ProjectId = id; _project = null; }
        if (section == CatalogSection.Chain) _settings.ChainId = id;
        await RefreshAll();
        if (section == CatalogSection.Employee) await OpenEmployeeAsync(id);
    }
    private static void Replace<T>(ObservableCollection<T> list, IEnumerable<T> values) { list.Clear(); foreach (var value in values) list.Add(value); }
    private void NotifyState()
    {
        NotifyNavigation();
        foreach (var p in new[] { nameof(IsIdle), nameof(HasPending), nameof(CanSelectWorkspace), nameof(CanEditDraft), nameof(CanEditCatalog), nameof(CanSaveRound), nameof(CanSaveResult), nameof(HasProject), nameof(HasChain), nameof(HasRound),
            nameof(SaveRoundLabel), nameof(SaveResultLabel), nameof(ProjectSummary), nameof(ProjectContext), nameof(EmployeeDescription), nameof(ChainSummary), nameof(CountLabel), nameof(ParentLabel), nameof(CurrentEmployeeLabel), nameof(RevisionLabel), nameof(RecordTitle), nameof(RecordMetadata), nameof(RecordInput), nameof(RecordBasePrompt), nameof(HistoricalOutput), nameof(HistoricalNote) }) OnPropertyChanged(p);
        foreach (var c in new[] { RefreshCommand, SaveRoundCommand, SaveResultCommand, ResolvePendingCommand, ContextCommand, SearchCommand, CompareCommand }) c?.RaiseCanExecuteChanged();
        foreach (var c in new[] { ContinueCommand, ReviseCommand, RootCommand, NewProjectCommand, EditProjectCommand, NewEmployeeCommand, EditEmployeeCommand, NewChainCommand, EditChainCommand, ConnectionCommand, KeysCommand }) c?.RaiseCanExecuteChanged();
    }
    private void ScheduleSave() { if (_loading || _disposed) return; _saveTimer.Stop(); _saveTimer.Start(); }
    public void Persist()
    {
        if (_disposed) return;
        _saveTimer.Stop();
        try { _settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { HasError = true; Status = "草稿未能写入设备：" + e.Message; }
    }
    private void PersistRequired() { _saveTimer.Stop(); _settings.Save(); }
    public void Dispose() { if (_disposed) return; Persist(); _disposed = true; _saveTimer.Stop(); _api.Dispose(); }
}
