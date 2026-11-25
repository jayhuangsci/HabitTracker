using System.Collections.ObjectModel;
using HabitTracker.ViewModels;
using PromptCrew.Core;

namespace PromptCrew.Desktop.ViewModels;

public sealed class EditorField(string key, string label, string value, bool multiline = false) : ViewModelBase
{
    private string _value = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public bool Multiline { get; } = multiline;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}
public sealed class CatalogEditor : ViewModelBase
{
    private readonly WorkspaceViewModel _workspace;
    private readonly CatalogSection _section;
    private readonly bool _edit;
    private RoleTemplate? _template;
    private string _status = "";
    private bool _busy;
    private Choice<string>? _state;
    public string Title { get; }
    public ObservableCollection<EditorField> Fields { get; } = [];
    public IReadOnlyList<RoleTemplate> Templates => _workspace.Templates;
    public bool ShowTemplates => _section == CatalogSection.Employee && !_edit;
    public Choice<string>[] States { get; }
    public Choice<string>? State { get => _state; set => SetProperty(ref _state, value); }
    public RoleTemplate? SelectedTemplate
    {
        get => _template;
        set
        {
            if (!SetProperty(ref _template, value) || value == null || !ShowTemplates) return;
            Put("name", value.Name); Put("role", value.Role); Put("responsibilities", value.Responsibilities);
            Put("basePrompt", value.BasePrompt); Put("skills", string.Join(", ", value.Skills));
        }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    public AsyncRelayCommand SaveCommand { get; }
    public event Action? Saved;
    public CatalogEditor(WorkspaceViewModel workspace, CatalogSection section, bool edit)
    {
        _workspace = workspace; _section = section; _edit = edit;
        Title = (edit ? "编辑" : "新建") + (section switch
        { CatalogSection.Project => "项目", CatalogSection.Employee => "员工", _ => "任务链" });
        SaveCommand = new(Save, () => !IsBusy);
        void Add(string key, string label, string value, bool multiline = false) => Fields.Add(new(key, label, value, multiline));
        if (section == CatalogSection.Project)
        {
            var p = edit ? workspace.SelectedProject : null;
            Add("name", "项目名称", p?.Name ?? ""); Add("goal", "项目目标", p?.Goal ?? "", true);
            Add("background", "背景资料", p?.Background ?? "", true); Add("constraints", "约束条件", p?.Constraints ?? "", true);
            States = Labels.ProjectStates.Select(x => new Choice<string>(x.Value.ToString(), x.Label)).ToArray();
            State = States.First(x => x.Value == (p?.Status ?? ProjectState.Active).ToString());
        }
        else if (section == CatalogSection.Employee)
        {
            var e = edit ? workspace.SelectedEmployee : null;
            Add("name", "员工名称", e?.Name ?? ""); Add("role", "角色", e?.Role ?? "");
            Add("responsibilities", "特定职责", e?.Responsibilities ?? "", true); Add("basePrompt", "员工基础提示词", e?.BasePrompt ?? "", true);
            Add("skills", "关联 Skills · 逗号分隔", string.Join(", ", e?.Skills ?? []));
            States = [new("true", "启用"), new("false", "停用")]; State = States.First(x => x.Value == (e?.IsActive ?? true).ToString().ToLowerInvariant());
        }
        else
        {
            var c = edit ? workspace.SelectedChain : null;
            Add("name", "任务链名称", c?.Name ?? ""); Add("goal", "本任务的目标", c?.Goal ?? "", true);
            Add("acceptance", "完成标准", c?.AcceptanceCriteria ?? "", true);
            States = Labels.ChainStates.Select(x => new Choice<string>(x.Value.ToString(), x.Label)).ToArray();
            State = States.First(x => x.Value == (c?.Status ?? ChainState.Active).ToString());
        }
    }
    private string Get(string key) => Fields.First(x => x.Key == key).Value;
    private void Put(string key, string value) => Fields.First(x => x.Key == key).Value = value;
    private async Task Save()
    {
        IsBusy = true; Status = "正在保存…";
        try
        {
            Guid id;
            if (_section == CatalogSection.Project)
            {
                var p = await _workspace.Api.SaveProject(_edit ? _workspace.SelectedProject?.Id : null,
                    new ProjectInput { Name = Get("name"), Goal = Get("goal"), Background = Get("background"), Constraints = Get("constraints"),
                        Status = Enum.Parse<ProjectState>(State!.Value), ExpectedVersion = _edit ? _workspace.SelectedProject?.Version : null });
                id = p.Id;
            }
            else if (_section == CatalogSection.Employee)
            {
                var e = await _workspace.Api.SaveEmployee(_workspace.SelectedProject!.Id, _edit ? _workspace.SelectedEmployee?.Id : null,
                    new EmployeeInput { Name = Get("name"), Role = Get("role"), Responsibilities = Get("responsibilities"), BasePrompt = Get("basePrompt"),
                        Skills = Get("skills").Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                        IsActive = bool.Parse(State!.Value), ExpectedVersion = _edit ? _workspace.SelectedEmployee?.Version : null });
                id = e.Id;
            }
            else
            {
                var c = await _workspace.Api.SaveChain(_workspace.SelectedProject!.Id, _edit ? _workspace.SelectedChain?.Id : null,
                    new ChainInput { Name = Get("name"), Goal = Get("goal"), AcceptanceCriteria = Get("acceptance"),
                        Status = Enum.Parse<ChainState>(State!.Value), ExpectedVersion = _edit ? _workspace.SelectedChain?.Version : null });
                id = c.Id;
            }
            await _workspace.CatalogSaved(_section, id); Saved?.Invoke();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { Status = e.Message; }
        finally { IsBusy = false; }
    }
}
