using System.IO;
using HabitTracker.ViewModels;
using PromptCrew.Client;

namespace PromptCrew.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private CancellationTokenSource? _operationSource;
    private Task _operationTask = Task.CompletedTask;
    private int _navigationSequence;
    private bool _navigating;
    private Action? _restoreWorkspace;
    private string _connectionState = "未连接";
    public string ConnectionState { get => _connectionState; private set => SetProperty(ref _connectionState, value); }
    public string ConnectionAddress => _settings.ApiUrl;
    public RelayCommand CancelCommand { get; private set; } = null!;
    private CancellationToken OperationToken => _operationSource?.Token ?? CancellationToken.None;

    private void NotifyConnection()
    {
        CancelCommand ??= new(_ => _operationSource?.Cancel(), () => IsBusy);
        CancelCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ConnectionAddress));
    }
    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> request)
    {
        var token = OperationToken;
        var result = await request(token);
        token.ThrowIfCancellationRequested(); // Also guard transports that ignore cancellation.
        if (_disposed) throw new OperationCanceledException(token);
        ConnectionState = "已连接";
        return result;
    }
    private async Task NavigateAsync(Func<Task> action)
    {
        if (_disposed || HasPending || IsBusy && !_navigating) return;
        var sequence = ++_navigationSequence;
        _operationSource?.Cancel();
        await _operationTask;
        if (sequence != _navigationSequence || _disposed) return;
        _navigating = true;
        try { await RunAsync(action); }
        finally { if (sequence == _navigationSequence) { _navigating = false; NotifyState(); } }
    }
    private Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        if (IsBusy || _disposed) return Task.CompletedTask;
        // Publish the task before the first await can complete, so navigation waits for cleanup.
        _operationTask = RunOperation(action, ct);
        return _operationTask;
    }
    private async Task RunOperation(Func<Task> action, CancellationToken ct)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _operationSource = source;
        IsBusy = true; HasError = false; Status = "正在更新工作台…";
        try
        {
            await action(); source.Token.ThrowIfCancellationRequested();
            CaptureWorkspace();
            if (Status == "正在更新工作台…") Status = "工作台已更新。";
        }
        catch (Exception e) when (e is CrewApiException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (_navigating) _restoreWorkspace?.Invoke();
            HasError = e is not OperationCanceledException || !source.IsCancellationRequested;
            Status = ConnectionFeedback.Describe(e, source.IsCancellationRequested);
            if (e is HttpRequestException or OperationCanceledException) ConnectionState = source.IsCancellationRequested ? "已取消" : "连接中断";
            if (e is CrewApiException { Status: 401 or 403 }) ConnectionState = "权限待确认";
        }
        finally { _operationSource = null; IsBusy = false; }
    }
    private void CaptureWorkspace()
    {
        var project = _project; var chain = _chain; var employee = _employee; var active = _activeEmployee;
        var round = _round; var result = _resultVersion; var draft = _draft; var graph = _graph; var overview = _overview;
        var employees = Employees.ToArray(); var chains = Chains.ToArray(); var lanes = EmployeeTimelines.ToArray(); var activities = _activities;
        var projects = Projects.ToArray(); var templates = Templates.ToArray();
        var admin = IsAdmin;
        var settingsProject = _settings.ProjectId; var settingsChain = _settings.ChainId; var settingsEmployee = _settings.EmployeeId;
        var tasks = _employeeTasks.ToArray(); var employeeChains = _employeeChains.ToArray();
        _restoreWorkspace = () =>
        {
            _loading = true;
            try
            {
                _project = project; _chain = chain; _employee = employee; _activeEmployee = active;
                _round = round; _resultVersion = result; _draft = draft; _graph = graph; _overview = overview;
                IsAdmin = admin;
                _settings.ProjectId = settingsProject; _settings.ChainId = settingsChain; _settings.EmployeeId = settingsEmployee;
                Replace(Employees, employees); Replace(Chains, chains); Replace(EmployeeTimelines, lanes); _activities = activities; BuildTree();
                Replace(Projects, projects); Replace(Templates, templates);
                _employeeTasks.Clear(); foreach (var item in tasks) _employeeTasks[item.Key] = item.Value;
                _employeeChains.Clear(); foreach (var item in employeeChains) _employeeChains[item.Key] = item.Value;
                foreach (var name in new[] { nameof(SelectedProject), nameof(SelectedChain), nameof(SelectedEmployee), nameof(SelectedRound), nameof(SelectedResultVersion) }) OnPropertyChanged(name);
                NotifyDraft(); NotifyNavigation();
            }
            finally { _loading = false; }
            Persist();
        };
    }
    public async Task TestConnectionAsync(string url, string keyFile, int timeoutSeconds, CancellationToken ct = default)
    {
        if (timeoutSeconds is < 1 or > 120) throw new ArgumentException("超时须为 1–120 秒。");
        using var candidate = new CrewClient(url, () => CrewClient.ReadKeyFile(keyFile), TimeSpan.FromSeconds(timeoutSeconds));
        await candidate.Session(ct);
    }
    public async Task ChangeConnection(string url, string keyFile, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        if (IsBusy) throw new InvalidOperationException("请先取消当前操作，再更改连接。");
        var normalized = CrewClient.Address(url).AbsoluteUri;
        if (HasPending && !string.Equals(CrewClient.Address(_settings.ApiUrl).AbsoluteUri, normalized, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("先确认待保存的请求，再切换到其他服务。");
        var timeout = timeoutSeconds ?? _settings.TimeoutSeconds;
        await TestConnectionAsync(normalized, keyFile, timeout, ct);
        ct.ThrowIfCancellationRequested();
        PersistRequired(); // Store the old workspace before switching servers.
        var candidate = new CrewClient(normalized, () => CrewClient.ReadKeyFile(keyFile), TimeSpan.FromSeconds(timeout));
        var previous = _api;
        var previousUrl = _settings.ApiUrl; var previousKey = _settings.KeyFile; var previousTimeout = _settings.TimeoutSeconds;
        var restore = _restoreWorkspace;
        _api = candidate; _settings.ApiUrl = normalized; _settings.KeyFile = keyFile; _settings.TimeoutSeconds = timeout;
        _project = null; _activeEmployee = null; _employeeChains.Clear();
        try
        {
            await RunAsync(RefreshAll, ct);
            ct.ThrowIfCancellationRequested();
            if (HasError) throw new InvalidOperationException(Status);
            _settings.Save(); previous.Dispose();
        }
        catch
        {
            _api = previous; _settings.ApiUrl = previousUrl; _settings.KeyFile = previousKey; _settings.TimeoutSeconds = previousTimeout;
            _restoreWorkspace = restore; restore?.Invoke(); candidate.Dispose(); _settings.Save();
            OnPropertyChanged(nameof(ConnectionAddress)); throw;
        }
    }
}
