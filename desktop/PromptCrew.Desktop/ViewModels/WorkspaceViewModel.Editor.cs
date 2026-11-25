using HabitTracker.ViewModels;
using PromptCrew.Client;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private int _leftOutputVersion, _rightOutputVersion;
    public int LeftOutputVersion { get => _leftOutputVersion; set => SetProperty(ref _leftOutputVersion, value); }
    public int RightOutputVersion { get => _rightOutputVersion; set => SetProperty(ref _rightOutputVersion, value); }
    private static Choice<int>[] OutputVersions(RoundDto? round) => new[] { new Choice<int>(0, "最新输出"), new Choice<int>(1, "v1 · 尚未输出") }
        .Concat((round?.Results ?? []).Select(x => new Choice<int>(x.Version, $"v{x.Version} · {Labels.State(x.Status)}"))).ToArray();
    public Choice<int>[] LeftOutputVersions => OutputVersions(_compareTarget);
    public Choice<int>[] RightOutputVersions => OutputVersions(_round);
    public string DraftInput { get => _draft.Input; set { _draft.Input = value; ChangedDraft(); } }
    public string DraftModel { get => _draft.Model; set { _draft.Model = value; ChangedDraft(); } }
    public string DraftTags { get => _draft.Tags; set { _draft.Tags = value; ChangedDraft(); } }
    public string DraftReferences { get => _draft.References; set { _draft.References = value; ChangedDraft(); } }
    public string DraftNotes { get => _draft.Notes; set { _draft.Notes = value; ChangedDraft(); } }
    public string DraftOutput { get => _draft.Output; set { _draft.Output = value; ChangedDraft(); } }
    public string DraftArtifacts { get => _draft.Artifacts; set { _draft.Artifacts = value; ChangedDraft(); } }
    public string ResultNote { get => _draft.ResultNote; set { _draft.ResultNote = value; ChangedDraft(); } }
    public RoundState ResultStatus { get => _draft.ResultStatus; set { _draft.ResultStatus = value; ChangedDraft(); } }
    private void ChangedDraft([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    { OnPropertyChanged(name); NotifyState(); ScheduleSave(); }
    private void NotifyDraft()
    {
        foreach (var name in new[] { nameof(DraftInput), nameof(DraftModel), nameof(DraftTags), nameof(DraftReferences), nameof(DraftNotes), nameof(DraftOutput), nameof(DraftArtifacts), nameof(ResultNote), nameof(ResultStatus) }) OnPropertyChanged(name);
        NotifyState();
    }
    private static string[] Split(string text) => text.Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
    private void Continue(bool revise)
    {
        if (_round == null || HasPending) return;
        _draft.ParentId = revise ? _round.ParentRoundId : _round.Id;
        _draft.RevisesId = revise ? _round.Id : null;
        if (revise)
        {
            _draft.Input = _round.Input; _draft.Model = _round.Model; _draft.Tags = string.Join(", ", _round.Tags);
            _draft.References = string.Join("\n", _round.ReferenceRoundIds); _draft.Notes = _round.Notes;
            SelectedEmployee = Employees.FirstOrDefault(x => x.Id == _round.EmployeeId);
        }
        NotifyDraft(); ScheduleSave();
        Status = revise ? "已复制到新草稿；保存会创建新轮次，保留原输入。" : "已选定父节点。可切换员工，编写下一轮提示词。";
    }
    public async Task ContinueSelectedAsync(bool revise = false)
    {
        Continue(revise); await Task.CompletedTask;
    }
    private async Task SaveRound()
    {
        if (_project == null || _chain == null || _employee == null) return;
        if (_draft.PendingRound == null)
        {
            _pendingAbsent = false;
            var references = Split(DraftReferences).Select(s => Guid.TryParse(s, out var id) ? id : throw new ArgumentException("成果引用须填写轮次 GUID，以逗号或换行分隔。")).ToArray();
            _draft.PendingRound = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = _employee.Id,
                ParentRoundId = _draft.ParentId, RevisesRoundId = _draft.RevisesId, Input = Rules.Text(DraftInput, "原始提示词", 100000, true),
                Model = DraftModel, Tags = Split(DraftTags), ReferenceRoundIds = references, Notes = DraftNotes, Source = EntrySource.Manual,
                ExpectedEmployeeVersion = _employee.Version };
        }
        PersistRequired(); NotifyState();
        var saved = await _api.SaveRound(_project.Id, _chain.Id, _draft.PendingRound, OperationToken);
        _draft.PendingRound = null; _draft.Input = ""; _draft.Notes = ""; _draft.RevisesId = null; _draft.ParentId = saved.Round.Id;
        PersistRequired(); ApplySavedRound(saved.Round); NotifyDraft();
        await RefreshSavedGraph(saved.Round.Id);
        Status = "输入已保存 · " + saved.Round.Id + (saved.Replayed ? " · 已确认原请求，无重复记录" : "");
    }
    private async Task SaveResult()
    {
        if (_project == null || _round == null) return;
        if (_draft.PendingResult == null)
        {
            _draft.PendingResultRoundId = _round.Id;
            _draft.PendingResult = new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = _round.Version,
                Output = DraftOutput, Status = ResultStatus, Artifacts = Split(DraftArtifacts), Note = ResultNote };
        }
        PersistRequired(); NotifyState();
        var saved = await _api.SaveResult(_project.Id, _draft.PendingResultRoundId!.Value, _draft.PendingResult, OperationToken);
        _draft.PendingResult = null; _draft.PendingResultRoundId = null; _draft.ResultNote = "";
        PersistRequired(); ApplySavedRound(saved.Round); NotifyDraft();
        await RefreshSavedGraph(saved.Round.Id);
        Status = $"结果版本 v{saved.Round.Version} 已追加，历史版本保留。";
    }
    private async Task ResolvePending()
    {
        if (_project == null) return;
        if (_draft.NewTaskId != null) { Status = "新任务请求已保留，请在快速记录面板重试保存。"; return; }
        if (_draft.PendingRound != null)
        {
            RoundDto? saved = null;
            try { saved = await Read(ct => _api.ByRequest(_project.Id, _draft.PendingRound.ClientRequestId, ct)); }
            catch (CrewApiException e) when (e.Status == 404) { }
            _pendingAbsent = saved == null;
            if (saved != null)
            {
                _draft.PendingRound = null;
                _draft.Input = ""; _draft.ParentId = saved.Id; _draft.RevisesId = null;
                await ReloadGraph(saved.Id); Status = "已找到原请求的记录，保存已确认。";
            }
            else Status = "服务中未找到该请求。草稿和原请求 ID 保留，请重试同一输入。";
        }
        if (_draft.PendingResult != null && _draft.PendingResultRoundId.HasValue)
        {
            var row = await Read(ct => _api.Round(_project.Id, _draft.PendingResultRoundId.Value, ct));
            var exists = row.Results.Any(x => x.ClientRequestId == _draft.PendingResult.ClientRequestId);
            _pendingAbsent = !exists;
            if (exists) { _draft.PendingResult = null; _draft.PendingResultRoundId = null; }
            await SelectRoundCore(row.Id);
            Status = exists ? "已找到原结果版本，保存已确认。" : "服务中未找到该结果请求。原请求 ID 保留，可重试同一结果。";
        }
        PersistRequired(); NotifyDraft();
    }
    private void ApplySavedRound(RoundDto row)
    {
        _round = row; SelectedResultVersion = row.Results.LastOrDefault();
        var activity = new ActivityDto(row.Id, row.ProjectId, row.ChainId, _chain?.Name ?? "", row.EmployeeId, row.Number,
            row.Input[..Math.Min(row.Input.Length, 160)], row.LatestOutput[..Math.Min(row.LatestOutput.Length, 220)],
            row.Status, row.Version, row.CreatedUtc, row.ParentRoundId, row.RevisesRoundId);
        var lane = EmployeeTimelines.FirstOrDefault(x => x.Id == row.EmployeeId);
        if (lane != null)
        {
            var exists = lane.Cards.Any(x => x.Id == row.Id);
            var records = lane.Cards.Where(x => x.Id != row.Id).Select(x => x.Record).Append(activity);
            EmployeeTimelines[EmployeeTimelines.IndexOf(lane)] = new(lane.Employee, records, lane.Total + (exists ? 0 : 1));
        }
        OnPropertyChanged(nameof(SelectedRound)); NotifyNavigation();
    }
    private async Task RefreshSavedGraph(Guid roundId)
    {
        try { await ReloadGraph(roundId); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or CrewApiException)
        { ConnectionState = "记录已保存 · 刷新待重试"; }
    }
    private async Task LoadContext()
    {
        if (_project == null || _chain == null || _employee == null) return;
        var refs = Split(DraftReferences).Select(s => Guid.TryParse(s, out var id) ? id : throw new ArgumentException("成果引用须填写有效轮次 GUID。")).ToArray();
        var context = await Read(ct => _api.Context(_project.Id, _chain.Id, _employee.Id, _draft.ParentId, refs, ct));
        CompiledContext = context.CompiledContext;
        ContextHeading = "草稿上下文 · " + ParentLabel;
        Status = $"已提取 {context.Ancestors.Length} 个祖先节点和 {context.References.Length} 个显式引用；不包含兄弟分支。";
    }
    private async Task Search()
    {
        if (_project == null) return;
        var page = await Read(ct => _api.Search(_project.Id, new(SearchText, SearchEmployee?.Id, null, SearchTag, SearchState?.Value, 0, 200), ct));
        Replace(SearchResults, page.Items);
        Status = $"找到 {page.Total} 轮记录" + (page.Total > page.Items.Length ? $"，当前显示前 {page.Items.Length} 轮。" : "。");
    }
    public Task OpenRoundAsync(RoundDto row) => _project?.Id == row.ProjectId ? OpenActivityAsync(row.Id)
        : RunAsync(() => throw new InvalidOperationException("请先切换到记录所属项目。"));
    public async Task PrepareComparisonAsync()
    {
        if (_project == null) return;
        await RunAsync(async () =>
        {
            var page = await Read(ct => _api.Search(_project.Id, new(ChainId: _chain?.Id, Take: 200), ct));
            Replace(ComparisonOptions, page.Items);
            if (_round != null && !ComparisonOptions.Any(x => x.Id == _round.Id)) ComparisonOptions.Insert(0, _round);
            ComparisonTarget = page.Items.FirstOrDefault(x => x.Id == _round?.RevisesRoundId)
                ?? page.Items.FirstOrDefault(x => x.Id != _round?.Id) ?? _round;
        });
    }
    private async Task Compare()
    {
        if (_project == null || _round == null || _compareTarget == null) return;
        var result = await Read(ct => _api.Compare(_project.Id, _compareTarget.Id, _round.Id,
            LeftOutputVersion == 0 ? null : LeftOutputVersion, RightOutputVersion == 0 ? null : RightOutputVersion, ct));
        InputDiff = result.InputDiff; OutputDiff = result.OutputDiff;
        Status = $"比较 {_compareTarget.Label} v{result.LeftOutputVersion} → {_round.Label} v{result.RightOutputVersion}。";
    }
}
