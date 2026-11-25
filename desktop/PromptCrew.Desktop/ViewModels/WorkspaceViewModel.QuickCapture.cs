using HabitTracker.ViewModels;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private bool _quickCaptureOpen;
    private string _savedNotice = "";
    public event Action? QuickCaptureOpened;
    public bool IsQuickCaptureOpen { get => _quickCaptureOpen; private set => SetProperty(ref _quickCaptureOpen, value); }
    public string QuickCaptureTitle => _employee == null ? "记录提示词" : $"为{_employee.Name}记录提示词";
    public string QuickTaskLabel => _chain == null ? "新任务 · 保存时创建" : $"任务：{_chain.Name}";
    public bool IsNewTask => _chain == null;
    public string NewTaskName { get => _draft.NewTaskName; set { _draft.NewTaskName = value; ChangedDraft(); } }
    public bool CanSaveQuick => !IsBusy && _project != null && _employee is { IsActive: true } && _draft.PendingResult == null
        && (!string.IsNullOrWhiteSpace(DraftInput) || _draft.PendingRound != null)
        && (_chain != null || !string.IsNullOrWhiteSpace(NewTaskName));
    public bool CanEditQuick => !IsBusy && !HasPending;
    public string SavedNotice { get => _savedNotice; private set { SetProperty(ref _savedNotice, value); OnPropertyChanged(nameof(HasSavedNotice)); } }
    public bool HasSavedNotice => SavedNotice.Length > 0;
    private bool _pendingAbsent;
    public RelayCommand ReleasePendingCommand { get; private set; } = null!;
    public RelayCommand ContinueInQuickCommand { get; private set; } = null!;
    public bool ReduceMotion { get => _settings.ReduceMotion; set { _settings.ReduceMotion = value; OnPropertyChanged(); Persist(); } }
    public RelayCommand QuickCaptureCommand { get; private set; } = null!;
    public RelayCommand CloseQuickCaptureCommand { get; private set; } = null!;
    public RelayCommand ResumeQuickCaptureCommand { get; private set; } = null!;
    public RelayCommand StartNewTaskCommand { get; private set; } = null!;
    public RelayCommand DismissNoticeCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveQuickCommand { get; private set; } = null!;

    private void NotifyQuickCapture()
    {
        if (QuickCaptureCommand == null)
        {
            QuickCaptureCommand = new(value => { if (value is Guid id) _ = OpenQuickCaptureAsync(id); }, () => CanSelectWorkspace);
            CloseQuickCaptureCommand = new(_ => { Persist(); IsQuickCaptureOpen = false; }, () => !IsBusy);
            ResumeQuickCaptureCommand = new(_ => { IsQuickCaptureOpen = true; QuickCaptureOpened?.Invoke(); }, () => !IsBusy && _employee != null);
            StartNewTaskCommand = new(_ => _ = NavigateAsync(async () =>
            {
                PersistRequired(); _loading = true;
                try { SelectedChain = null; } finally { _loading = false; }
                await LoadChain();
            }), () => CanSelectWorkspace);
            DismissNoticeCommand = new(_ => SavedNotice = "");
            ContinueInQuickCommand = new(_ => { Continue(false); IsQuickCaptureOpen = true; QuickCaptureOpened?.Invoke(); }, () => !IsBusy && _round != null && !HasPending);
            ReleasePendingCommand = new(_ => _ = RunAsync(async () =>
            {
                _draft.PendingRound = null; _draft.PendingResult = null; _draft.PendingResultRoundId = null;
                _pendingAbsent = false; PersistRequired();
                if (_project != null) Replace(Employees, await Read(ct => _api.Employees(_project.Id, ct)));
                _employee = Employees.FirstOrDefault(e => e.Id == _activeEmployee?.Id);
                if (_round != null) await SelectRoundCore(_round.Id);
                Status = "已恢复为可编辑草稿。下一次保存会创建新请求；原始输入仍保留。";
                NotifyDraft();
            }), () => !IsBusy && HasPending && _pendingAbsent);
            SaveQuickCommand = new(() => RunAsync(SaveQuick), () => CanSaveQuick);
        }
        foreach (var name in new[] { nameof(QuickCaptureTitle), nameof(QuickTaskLabel), nameof(IsNewTask), nameof(NewTaskName), nameof(CanSaveQuick), nameof(CanEditQuick) }) OnPropertyChanged(name);
        QuickCaptureCommand.RaiseCanExecuteChanged(); CloseQuickCaptureCommand.RaiseCanExecuteChanged(); StartNewTaskCommand.RaiseCanExecuteChanged();
        ResumeQuickCaptureCommand.RaiseCanExecuteChanged(); SaveQuickCommand.RaiseCanExecuteChanged();
        ReleasePendingCommand.RaiseCanExecuteChanged(); ContinueInQuickCommand.RaiseCanExecuteChanged();
    }
    public async Task OpenQuickCaptureAsync(Guid employeeId)
    {
        await OpenEmployeeAsync(employeeId);
        if (_employee?.Id != employeeId || HasError || _disposed) return;
        if (!HasPending && string.IsNullOrWhiteSpace(DraftInput) && _round?.ChainId == _chain?.Id && _draft.ParentId == null)
        { _draft.ParentId = _round?.Id; NotifyDraft(); ScheduleSave(); }
        IsQuickCaptureOpen = true; SavedNotice = ""; QuickCaptureOpened?.Invoke();
    }
    private async Task SaveQuick()
    {
        if (_project == null || _employee == null) return;
        if (_chain == null)
        {
            _draft.NewTaskId ??= Guid.NewGuid();
            PersistRequired(); NotifyState();
            var created = await _api.SaveChain(_project.Id, null, new ChainInput
            { CreateId = _draft.NewTaskId, Name = NewTaskName, Goal = NewTaskName }, OperationToken);
            OperationToken.ThrowIfCancellationRequested();
            // Move the same draft only after the server has confirmed the task ID.
            var looseKey = _settings.ApiUrl.TrimEnd('/').ToLowerInvariant() + "|new|" + _employee.Id;
            var key = _settings.ApiUrl.TrimEnd('/').ToLowerInvariant() + "|" + created.Id + "|" + _employee.Id;
            _settings.Drafts[key] = _draft; _settings.Drafts.Remove(looseKey);
            _draft.NewTaskId = null;
            _loading = true;
            try { Chains.Insert(0, created); SelectedChain = created; } finally { _loading = false; }
            _settings.ChainId = created.Id; PersistRequired();
        }
        await SaveRound();
        IsQuickCaptureOpen = false;
        SavedNotice = $"已为{_employee.Name}保存新记录 · 待输出";
        NotifyNavigation();
    }
}
