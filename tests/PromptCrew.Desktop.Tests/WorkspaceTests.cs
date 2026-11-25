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
