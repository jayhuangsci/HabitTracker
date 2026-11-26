using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HabitTracker.ViewModels;
using PromptCrew.Api.Tests;
using PromptCrew.Client;
using PromptCrew.Core;
using PromptCrew.Desktop;
using PromptCrew.Desktop.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace PromptCrew.Desktop.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public async Task RealWindowCommandsPreserveBranchesVersionsDraftsAndUncertainRequests()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PromptCrew.Desktop;component/PresentationResources.xaml") });
                    using var factory = new CrewFixture();
                    using var api = new CrewClient(factory.Admin());
                    var p = await api.SaveProject(null, new() { Name = "桌面验收", Goal = "战斗系统" });
                    var template = RoleTemplates.All[0];
                    var e = await api.SaveEmployee(p.Id, null, new() { Name = template.Name, Role = template.Role, Responsibilities = template.Responsibilities, BasePrompt = template.BasePrompt });
                    var c = await api.SaveChain(p.Id, null, new() { Name = "战斗", Goal = "完整交接" });
                    var path = Path.Combine(factory.DirectoryPath, "desktop.json");
                    var settings = new DesktopSettings { FilePath = path };
                    Guid root, branch;
                    using (var vm = new WorkspaceViewModel(settings, new CrewClient(factory.Admin())))
                    {
                        await vm.InitializeAsync(); Assert.False(vm.HasError, vm.Status);
                        var window = new MainWindow(vm);
                        window.Measure(new Size(1440, 920)); window.Arrange(new Rect(0, 0, 1440, 920)); window.UpdateLayout();
                        vm.DraftInput = "  战斗规则\n保留原始输入  "; vm.DraftModel = "test"; vm.DraftTags = "战斗, 回合制";
                        await Execute(vm.SaveRoundCommand, vm); root = vm.SelectedRound!.Id;
                        Assert.Equal("  战斗规则\n保留原始输入  ", vm.RecordInput);
                        vm.DraftOutput = "规则一"; await Execute(vm.SaveResultCommand, vm);
                        vm.DraftOutput = "规则二"; vm.ResultNote = "修正数值"; await Execute(vm.SaveResultCommand, vm);
                        Assert.Equal(2, vm.SelectedRound!.Results.Length);
                        Assert.Equal("规则一", vm.SelectedRound.Results[0].Output);
                        await vm.PrepareComparisonAsync(); vm.ComparisonTarget = vm.SelectedRound;
                        vm.LeftOutputVersion = 2; vm.RightOutputVersion = 3;
                        await Execute(vm.CompareCommand, vm);
                        Assert.Contains("− 规则一", vm.OutputDiff); Assert.Contains("+ 规则二", vm.OutputDiff);
                        vm.ContinueCommand.Execute(null); vm.DraftInput = "探索即时制";
                        await Execute(vm.SaveRoundCommand, vm); branch = vm.SelectedRound!.Id;
                        Assert.Equal(root, vm.SelectedRound.ParentRoundId);
                        vm.ReviseCommand.Execute(null); vm.DraftInput = "探索即时制修订版";
                        await Execute(vm.SaveRoundCommand, vm);
                        Assert.Equal(branch, vm.SelectedRound!.RevisesRoundId); Assert.Equal(root, vm.SelectedRound.ParentRoundId);
                        vm.DraftOutput = "未保存的修订输出";
                        var revision = vm.SelectedRound.Id;
                        await vm.SelectRoundAsync(root); await vm.SelectRoundAsync(revision);
                        Assert.Equal("未保存的修订输出", vm.DraftOutput);
                        await vm.PrepareComparisonAsync(); vm.ComparisonTarget = await api.Round(p.Id, branch);
                        await Execute(vm.CompareCommand, vm); Assert.Contains("+ 探索即时制修订版", vm.InputDiff);
                        await Execute(vm.ContextCommand, vm); Assert.Contains("战斗规则", vm.CompiledContext);
                        vm.SearchText = "规则一"; await Execute(vm.SearchCommand, vm); Assert.Equal(root, Assert.Single(vm.SearchResults).Id);
                        vm.DraftInput = "尚未保存的草稿"; vm.Persist();
                    }
                    using (var reopened = new WorkspaceViewModel(DesktopSettings.Load(path), new CrewClient(factory.Admin())))
                    {
                        await reopened.InitializeAsync(); Assert.False(reopened.HasError, reopened.Status);
                        Assert.Equal("尚未保存的草稿", reopened.DraftInput);
                        Assert.Equal(2, reopened.Roots[0].Children.Count);
                    }
                    var lost = new LoseReplyHandler { InnerHandler = factory.Server.CreateHandler() };
                    var http = new HttpClient(lost) { BaseAddress = new Uri("http://localhost/") };
                    http.DefaultRequestHeaders.Add("X-Api-Key", CrewFixture.TestKey);
                    Guid request;
                    using (var uncertain = new WorkspaceViewModel(DesktopSettings.Load(path), new CrewClient(http)))
                    {
                        await uncertain.InitializeAsync(); uncertain.DraftInput = "服务已保存但响应丢失";
                        await Execute(uncertain.SaveRoundCommand, uncertain, expectError: true);
                        Assert.True(uncertain.HasPending); Assert.False(uncertain.CanEditDraft);
                        request = uncertain.Settings.Drafts.Values.Single().PendingRound!.ClientRequestId;
                    }
                    using (var retried = new WorkspaceViewModel(DesktopSettings.Load(path), new CrewClient(factory.Admin())))
                    {
                        await retried.InitializeAsync(); Assert.True(retried.HasPending);
                        await Execute(retried.SaveRoundCommand, retried);
                        Assert.False(retried.HasPending); Assert.Equal(request, retried.SelectedRound!.ClientRequestId);
                        Assert.Single((await api.Search(p.Id, new(Text: "服务已保存但响应丢失"))).Items);
                    }
                    await VerifyEmployeeOverview();
                    await VerifyQuickCapture();
                    await VerifyCancelledNavigation();
                    await VerifyResultRecovery();
                    await VerifyConnectionCancellation();
                    completion.SetResult();
                }
                catch (Exception e) { completion.TrySetException(e); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(await Task.Run(() => thread.Join(TimeSpan.FromSeconds(75))), "Desktop flow exceeded 75 seconds.");
        await completion.Task;
    }
    private static async Task Execute(AsyncRelayCommand command, WorkspaceViewModel vm, bool expectError = false)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null);
        var timer = Stopwatch.StartNew();
        while (vm.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
        Assert.False(vm.IsBusy); Assert.Equal(expectError, vm.HasError);
    }
    private static async Task VerifyEmployeeOverview()
    {
        using var factory = new CrewFixture(); using var api = new CrewClient(factory.Admin());
        var project = await api.SaveProject(null, new() { Name = "按员工验收", Goal = "看到每人的持续开发过程" });
        var team = new List<EmployeeDto>();
        foreach (var t in RoleTemplates.All)
            team.Add(await api.SaveEmployee(project.Id, null, new() { Name = t.Name, Role = t.Role, Responsibilities = t.Responsibilities, BasePrompt = t.BasePrompt, Skills = t.Skills }));
        var battle = await api.SaveChain(project.Id, null, new() { Name = "战斗原型", Goal = "完成战斗" });
        var polish = await api.SaveChain(project.Id, null, new() { Name = "试玩修订", Goal = "根据试玩继续改进" });
        var records = new Dictionary<Guid, List<RoundDto>>();
        foreach (var employee in team)
        {
            records[employee.Id] = [];
            for (var i = 0; i < 3; i++)
            {
                var references = i == 2 && employee.Id != team[0].Id ? new[] { records[team[0].Id][0].Id } : Array.Empty<Guid>();
                var chain = i == 2 ? polish : battle;
                var saved = await api.SaveRound(project.Id, chain.Id, new() { ClientRequestId = Guid.NewGuid(), EmployeeId = employee.Id,
                    Input = $"{employee.Name}的第{i + 1}轮实际开发输入", ParentRoundId = i == 1 ? records[employee.Id][0].Id : null, ReferenceRoundIds = references });
                var result = await api.SaveResult(project.Id, saved.Round.Id, new() { ClientRequestId = Guid.NewGuid(), ExpectedVersion = saved.Round.Version,
                    Output = $"{employee.Name}的成果{i + 1}" });
                records[employee.Id].Add(result.Round);
            }
        }
        var file = Path.Combine(factory.DirectoryPath, "overview-desktop.json");
        using (var vm = new WorkspaceViewModel(new DesktopSettings { FilePath = file }, new CrewClient(factory.Admin())))
        {
            await vm.InitializeAsync(); Assert.False(vm.HasError, vm.Status); Assert.True(vm.IsOverview);
            var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            window.Show();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            while (vm.IsBusy) await Task.Delay(10);
            window.UpdateLayout();
            async Task ClickBoundButton(ICommand command, Guid id)
            {
                window.UpdateLayout();
                var button = Visuals(window).OfType<Button>().First(x => ReferenceEquals(x.Command, command) && x.CommandParameter is Guid value && value == id);
                Assert.True(button.Command!.CanExecute(button.CommandParameter)); button.Command.Execute(button.CommandParameter);
                var wait = Stopwatch.StartNew(); while (vm.IsBusy && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                Assert.False(vm.IsBusy); Assert.False(vm.HasError, vm.Status);
            }
            Assert.Equal(4, vm.EmployeeTimelines.Count);
            foreach (var lane in vm.EmployeeTimelines)
            {
                Assert.Equal(3, lane.Cards.Count);
                Assert.Equal(records[lane.Id].Select(x => x.Id), lane.Cards.Select(x => x.Id));
                Assert.All(lane.Cards, card => Assert.Equal(lane.Id, card.Record.EmployeeId));
                Assert.False(lane.Cards[0].IsLast); Assert.True(lane.Cards[^1].IsLast);
                Assert.Contains("实际开发输入", lane.Cards[0].Title); Assert.Contains("成果", lane.Cards[0].Summary);
            }
            await ClickBoundButton(vm.OpenEmployeeCommand, team[0].Id); Assert.False(vm.IsOverview); Assert.Equal(team[0].Id, vm.ActiveEmployee!.Id);
            vm.DraftInput = "设计师自己的未保存草稿"; vm.Persist();
            await vm.OpenEmployeeAsync(team[1].Id); Assert.NotEqual("设计师自己的未保存草稿", vm.DraftInput);
            vm.DraftInput = "程序员自己的未保存草稿"; vm.Persist();
            await vm.OpenEmployeeAsync(team[0].Id); Assert.Equal("设计师自己的未保存草稿", vm.DraftInput);
            var target = records[team[1].Id][2];
            vm.GoOverview(); window.Width = 1160; window.Height = 800;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.Measure(new Size(1160, 800)); window.Arrange(new Rect(0, 0, 1160, 800)); window.UpdateLayout();
            var overview = (UserControl)window.FindName("OverviewPanel"); var scroll = (ScrollViewer)overview.FindName("HistoryScroll");
            scroll.ScrollToHorizontalOffset(80); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var offset = scroll.HorizontalOffset; Assert.True(offset > 0, $"No horizontal overflow: extent {scroll.ExtentWidth}, viewport {scroll.ViewportWidth}");
            await ClickBoundButton(vm.OpenActivityCommand, target.Id); Assert.False(vm.HasError, vm.Status);
            Assert.Equal(team[1].Id, vm.ActiveEmployee!.Id); Assert.Equal(polish.Id, vm.SelectedChain!.Id);
            Assert.Equal(target.Id, vm.SelectedRound!.Id); Assert.Equal(target.ReferenceRoundIds, vm.SelectedRound.ReferenceRoundIds);
            var employeePanel = (UserControl)window.FindName("EmployeePanel");
            Assert.Equal(1, ((TabControl)employeePanel.FindName("EmployeeTabs")).SelectedIndex);
            Assert.Contains("试玩修订", vm.RecordRelations); Assert.Equal(2, vm.ParticipatingChains.Length);
            await vm.ViewRecordContextAsync(); Assert.Contains(records[team[0].Id][0].Input, vm.CompiledContext);
            vm.GoOverview(); Assert.True(vm.IsOverview); Assert.All(vm.EmployeeTimelines, x => Assert.False(x.IsSelected));
            window.UpdateLayout(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(offset, scroll.HorizontalOffset);
            var other = await api.SaveProject(null, new() { Name = "空项目", Goal = "检查切换隔离" });
            await vm.InitializeAsync(); vm.SelectedProject = vm.Projects.First(x => x.Id == other.Id);
            var timer = Stopwatch.StartNew(); while (vm.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Assert.True(vm.IsOverview); Assert.Empty(vm.EmployeeTimelines); Assert.Null(vm.ActiveEmployee);
            window.Close();
        }
        using var restored = new WorkspaceViewModel(DesktopSettings.Load(file), new CrewClient(factory.Admin()));
        await restored.InitializeAsync(); restored.SelectedProject = restored.Projects.First(x => x.Id == project.Id);
        while (restored.IsBusy) await Task.Delay(10);
        await restored.OpenEmployeeAsync(team[0].Id); Assert.Equal("设计师自己的未保存草稿", restored.DraftInput);
        await restored.OpenEmployeeAsync(team[1].Id); Assert.Equal("程序员自己的未保存草稿", restored.DraftInput);
        var oldSettings = new DesktopSettings { FilePath = Path.Combine(factory.DirectoryPath, "legacy-desktop.json"), ProjectId = project.Id, ChainId = battle.Id };
        var retryId = Guid.NewGuid();
        var oldKey = oldSettings.ApiUrl.TrimEnd('/').ToLowerInvariant() + "|" + battle.Id;
        oldSettings.Drafts[oldKey] = new() { EmployeeId = team[1].Id, Input = "旧版程序员草稿", PendingRound = new RoundInput
            { ClientRequestId = retryId, EmployeeId = team[1].Id, Input = "旧版程序员草稿" } };
        using var migrated = new WorkspaceViewModel(oldSettings, new CrewClient(factory.Admin()));
        await migrated.InitializeAsync(); Assert.False(migrated.HasError, migrated.Status);
        Assert.Equal(team[1].Id, migrated.ActiveEmployee!.Id); Assert.Equal("旧版程序员草稿", migrated.DraftInput);
        Assert.True(migrated.HasPending); Assert.DoesNotContain(oldKey, oldSettings.Drafts.Keys);
        Assert.Equal(retryId, Assert.Single(oldSettings.Drafts.Values).PendingRound!.ClientRequestId);
    }
    private static async Task VerifyQuickCapture()
    {
        using var app = new CrewFixture(); using var api = new CrewClient(app.Admin());
        var project = await api.SaveProject(null, new() { Name = "快捷记录验收" });
        var team = new List<EmployeeDto>();
        foreach (var t in RoleTemplates.All.Take(2)) team.Add(await api.SaveEmployee(project.Id, null, new()
        { Name = t.Name, Role = t.Role, Responsibilities = t.Responsibilities, BasePrompt = t.BasePrompt }));
        var file = Path.Combine(app.DirectoryPath, "quick.json");
        var handler = new FailBeforeWriteHandler { InnerHandler = app.Server.CreateHandler() };
        var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", CrewFixture.TestKey);
        Guid savedId;
        using (var vm = new WorkspaceViewModel(new DesktopSettings { FilePath = file }, new CrewClient(http)))
        {
            await vm.InitializeAsync();
            var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 940, Height = 700 };
            window.Show(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            while (vm.IsBusy) await Task.Delay(10);
            window.UpdateLayout();
            var plus = Visuals(window).OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.QuickCaptureCommand) && b.CommandParameter is Guid id && id == team[1].Id && b.Content?.ToString() == "+");
            plus.Command!.Execute(plus.CommandParameter); while (vm.IsBusy) await Task.Delay(10);
            Assert.True(vm.IsQuickCaptureOpen); Assert.Equal(team[1].Id, vm.SelectedEmployee!.Id); Assert.True(vm.IsNewTask);
            async Task CaptureStep(string name)
            {
                if (Environment.GetEnvironmentVariable("PROMPTCREW_DEMO_CAPTURE_DIR") is not { Length: > 0 } directory) return;
                await Task.Delay(230); window.UpdateLayout();
                window.Capture(Path.Combine(directory, name + ".png"));
            }
            await CaptureStep("quick-01-open");
            vm.NewTaskName = "快速创建任务"; vm.DraftInput = "程序员实现战斗原型"; vm.Persist();
            await CaptureStep("quick-02-input");
            var save = Visuals(window).OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveQuickCommand));
            Assert.True(save.Command!.CanExecute(null)); save.Command.Execute(null); save.Command.Execute(null);
            while (vm.IsBusy) await Task.Delay(10);
            Assert.False(vm.HasError, vm.Status); Assert.False(vm.IsQuickCaptureOpen); Assert.True(vm.HasSavedNotice);
            var row = vm.SelectedRound!; savedId = row.Id;
            Assert.Equal(RoundState.PendingOutput, row.Status); Assert.Equal(team[1].Id, row.EmployeeId);
            Assert.Equal(savedId, Assert.Single(vm.ActiveTimeline!.Cards).Id); Assert.Single(await api.Chains(project.Id));
            Assert.Single((await api.Search(project.Id, new())).Items);
            await CaptureStep("quick-03-saved");
            await vm.OpenQuickCaptureAsync(team[1].Id); Assert.Equal(row.Id, vm.Settings.Drafts.Values.Single(d => d.EmployeeId == team[1].Id && d.ParentId != null).ParentId);
            vm.DraftInput = "继续实现技能冷却"; handler.FailNext = true;
            await Execute(vm.SaveQuickCommand, vm, expectError: true);
            var pending = vm.Settings.Drafts.Values.Single(d => d.PendingRound != null).PendingRound!;
            await Execute(vm.ResolvePendingCommand, vm); Assert.True(vm.HasPending);
            Assert.False(vm.ReleasePendingCommand.CanExecute(null)); // A 404 cannot rule out a late commit.
            Assert.Equal(pending.ClientRequestId, vm.Settings.Drafts.Values.Single(d => d.PendingRound != null).PendingRound!.ClientRequestId);
            Assert.Equal("继续实现技能冷却", vm.DraftInput); vm.Persist();
            window.Close();
        }
        using (var restored = new WorkspaceViewModel(DesktopSettings.Load(file), new CrewClient(app.Admin())))
        {
            await restored.InitializeAsync(); Assert.True(restored.HasPending);
            await Execute(restored.SaveQuickCommand, restored);
            Assert.False(restored.HasError, restored.Status); Assert.False(restored.HasPending);
            Assert.Equal(savedId, restored.SelectedRound!.ParentRoundId); Assert.Equal(2, restored.ActiveTimeline!.Cards.Count);
            // A historical node, rather than a neighboring employee card, owns the branch.
            await restored.OpenActivityAsync(savedId); restored.ContinueInQuickCommand.Execute(null);
            Assert.True(restored.IsQuickCaptureOpen); Assert.Contains("#01", restored.ParentLabel);
            restored.DraftInput = "从旧节点探索另一方案";
            await Execute(restored.SaveQuickCommand, restored); Assert.Equal(savedId, restored.SelectedRound!.ParentRoundId);
            await restored.OpenQuickCaptureAsync(team[0].Id); restored.DraftInput = "设计师独立草稿"; restored.Persist();
            await restored.OpenQuickCaptureAsync(team[1].Id); Assert.NotEqual("设计师独立草稿", restored.DraftInput);
            await restored.OpenQuickCaptureAsync(team[0].Id); Assert.Equal("设计师独立草稿", restored.DraftInput);
            restored.StartNewTaskCommand.Execute(null); while (restored.IsBusy) await Task.Delay(10);
            restored.NewTaskName = "另一个任务"; restored.DraftInput = "新任务草稿"; restored.Persist();
            var currentProject = (await api.Projects()).Single();
            var archived = await api.SaveProject(project.Id, new() { Name = currentProject.Name, Status = ProjectState.Archived, ExpectedVersion = currentProject.Version });
            await Execute(restored.SaveQuickCommand, restored, true);
            Assert.True(restored.HasPending); Assert.True(restored.ReleasePendingCommand.CanExecute(null));
            restored.ReleasePendingCommand.Execute(null); while (restored.IsBusy) await Task.Delay(10);
            Assert.False(restored.HasPending); Assert.Equal("新任务草稿", restored.DraftInput);
            await api.SaveProject(project.Id, new() { Name = archived.Name, Status = ProjectState.Active, ExpectedVersion = archived.Version });
        }
        using var recovery = new WorkspaceViewModel(DesktopSettings.Load(file), new CrewClient(app.Admin()));
        await recovery.InitializeAsync(); Assert.True(recovery.IsNewTask); Assert.Equal("新任务草稿", recovery.DraftInput); Assert.Equal("另一个任务", recovery.NewTaskName);
    }
    private static async Task VerifyCancelledNavigation()
    {
        using var app = new CrewFixture(); using var api = new CrewClient(app.Admin());
        var first = await api.SaveProject(null, new() { Name = "原项目" });
        var second = await api.SaveProject(null, new() { Name = "目标项目" });
        var third = await api.SaveProject(null, new() { Name = "第三项目" });
        var t = RoleTemplates.All[0];
        foreach (var p in new[] { first, second, third }) await api.SaveEmployee(p.Id, null, new() { Name = p.Name + "员工", Role = t.Role, Responsibilities = t.Responsibilities, BasePrompt = t.BasePrompt });
        var gate = new GateReadHandler { InnerHandler = app.Server.CreateHandler() };
        var http = new HttpClient(gate) { BaseAddress = new("http://localhost/") }; http.DefaultRequestHeaders.Add("X-Api-Key", CrewFixture.TestKey);
        using var vm = new WorkspaceViewModel(new DesktopSettings { FilePath = Path.Combine(app.DirectoryPath, "switch.json"), ProjectId = second.Id }, new CrewClient(http));
        await vm.InitializeAsync(); gate.Block = true;
        vm.SelectedProject = vm.Projects.First(p => p.Id == first.Id); await gate.Entered.Task;
        Assert.True(vm.CanSelectWorkspace);
        vm.SelectedProject = vm.Projects.First(p => p.Id == second.Id); gate.Release.SetResult();
        for (var i = 0; i < 200 && (vm.IsBusy || vm.Employees.Any(e => e.ProjectId != second.Id)); i++) await Task.Delay(10);
        Assert.False(vm.HasError, vm.Status); Assert.Equal(second.Id, vm.SelectedProject!.Id);
        Assert.All(vm.Employees, e => Assert.Equal(second.Id, e.ProjectId)); Assert.Equal("目标项目员工", vm.ActiveEmployee!.Name);
        gate.Arm();
        vm.SelectedProject = vm.Projects.First(p => p.Id == first.Id); await gate.Entered.Task;
        vm.SelectedProject = vm.Projects.First(p => p.Id == third.Id); gate.Release.SetResult();
        for (var i = 0; i < 200 && (vm.IsBusy || vm.ActiveEmployee?.ProjectId != third.Id); i++) await Task.Delay(10);
        Assert.Equal(third.Id, vm.SelectedProject!.Id); Assert.All(vm.Employees, e => Assert.Equal(third.Id, e.ProjectId));
        vm.DraftInput = "取消切换前的草稿"; vm.Persist();
        gate.Arm();
        vm.SelectedProject = vm.Projects.First(p => p.Id == first.Id); await gate.Entered.Task;
        vm.CancelCommand.Execute(null); gate.Release.SetResult();
        while (vm.IsBusy) await Task.Delay(10);
        Assert.Equal(third.Id, vm.SelectedProject!.Id); Assert.Equal("取消切换前的草稿", vm.DraftInput);
        Assert.All(vm.Employees, e => Assert.Equal(third.Id, e.ProjectId));
    }
    private static async Task VerifyResultRecovery()
    {
        using var app = new CrewFixture(); using var api = new CrewClient(app.Admin());
        var project = await api.SaveProject(null, new() { Name = "输出恢复" }); var template = RoleTemplates.All[0];
        var employee = await api.SaveEmployee(project.Id, null, new() { Name = template.Name, Role = template.Role, Responsibilities = template.Responsibilities, BasePrompt = template.BasePrompt });
        var chain = await api.SaveChain(project.Id, null, new() { Name = "输出任务", Goal = "不重复" });
        var round = (await api.SaveRound(project.Id, chain.Id, new() { ClientRequestId = Guid.NewGuid(), EmployeeId = employee.Id, Input = "实现原型" })).Round;
        var handler = new ResultFaultHandler { InnerHandler = app.Server.CreateHandler() };
        var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") }; http.DefaultRequestHeaders.Add("X-Api-Key", CrewFixture.TestKey);
        using var vm = new WorkspaceViewModel(new DesktopSettings { FilePath = Path.Combine(app.DirectoryPath, "results.json") }, new CrewClient(http));
        await vm.InitializeAsync(); vm.DraftOutput = "成果一"; handler.FailBefore = true;
        await Execute(vm.SaveResultCommand, vm, true); await Execute(vm.ResolvePendingCommand, vm);
        Assert.False(vm.ReleasePendingCommand.CanExecute(null));
        await Execute(vm.SaveResultCommand, vm);
        vm.DraftOutput = "成果二"; handler.LoseAfter = true;
        await Execute(vm.SaveResultCommand, vm, true); Assert.False(vm.ReleasePendingCommand.CanExecute(null));
        await Execute(vm.ResolvePendingCommand, vm); Assert.False(vm.HasPending); Assert.Equal(2, vm.SelectedRound!.Results.Length);
        // A first attempt rejected by optimistic concurrency can safely return to editing.
        await api.SaveResult(project.Id, round.Id, new() { ClientRequestId = Guid.NewGuid(), ExpectedVersion = vm.SelectedRound.Version, Output = "远端成果" });
        vm.DraftOutput = "本地修正"; await Execute(vm.SaveResultCommand, vm, true);
        Assert.True(vm.ReleasePendingCommand.CanExecute(null)); vm.ReleasePendingCommand.Execute(null); while (vm.IsBusy) await Task.Delay(10);
        Assert.Equal("本地修正", vm.DraftOutput); await Execute(vm.SaveResultCommand, vm);
        Assert.Equal(4, vm.SelectedRound!.Results.Length); Assert.Equal("远端成果", vm.SelectedRound.Results[2].Output);
    }
    private static async Task VerifyConnectionCancellation()
    {
        using var app = new CrewFixture(); using var api = new CrewClient(app.Admin());
        var project = await api.SaveProject(null, new() { Name = "连接取消原项目" });
        using var vm = new WorkspaceViewModel(new DesktopSettings { FilePath = Path.Combine(app.DirectoryPath, "connection.json") }, new CrewClient(app.Admin()));
        await vm.InitializeAsync(); var originalUrl = vm.Settings.ApiUrl;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var remote = builder.Build(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        remote.MapGet("/api/session", () => new SessionDto(false, "测试连接", []));
        remote.MapGet("/api/employee-templates", () => RoleTemplates.All);
        remote.MapGet("/api/projects", async (HttpContext context) => { entered.TrySetResult(); await Task.Delay(10000, context.RequestAborted); return Array.Empty<ProjectDto>(); });
        await remote.StartAsync();
        var address = remote.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var key = Path.Combine(app.DirectoryPath, "candidate.key"); File.WriteAllText(key, CrewFixture.TestKey);
        using var cancel = new CancellationTokenSource();
        var change = vm.ChangeConnection(address, key, ct: cancel.Token); await entered.Task; cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);
        Assert.Equal(originalUrl, vm.Settings.ApiUrl); Assert.Equal(project.Id, vm.SelectedProject!.Id); Assert.True(vm.IsAdmin);
        Assert.Equal(project.Id, (await vm.Api.Projects()).Single().Id);
        await remote.StopAsync();
    }
    private sealed class ResultFaultHandler : DelegatingHandler
    {
        public bool FailBefore { get; set; }
        public bool LoseAfter { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resultWrite = request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/results");
            if (resultWrite && FailBefore) { FailBefore = false; throw new HttpRequestException("Before commit"); }
            var response = await base.SendAsync(request, ct);
            if (resultWrite && LoseAfter) { LoseAfter = false; response.Dispose(); throw new HttpRequestException("After commit"); }
            return response;
        }
    }
    private sealed class FailBeforeWriteHandler : DelegatingHandler
    {
        public bool FailNext { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (FailNext && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/rounds"))
            { FailNext = false; throw new HttpRequestException("Disconnected before request reached the server"); }
            return base.SendAsync(request, ct);
        }
    }
    private sealed class GateReadHandler : DelegatingHandler
    {
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() { Block = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Block && request.RequestUri!.AbsolutePath.EndsWith("/employees"))
            { Block = false; Entered.TrySetResult(); await Release.Task; }
            return await base.SendAsync(request, ct);
        }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class LoseReplyHandler : DelegatingHandler
    {
        private bool _lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (!_lost && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/rounds"))
            { _lost = true; response.Dispose(); throw new HttpRequestException("Simulated lost response after commit"); }
            return response;
        }
    }
}
