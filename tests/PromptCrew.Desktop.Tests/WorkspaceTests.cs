using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
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
                    completion.SetResult();
                }
                catch (Exception e) { completion.TrySetException(e); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(await Task.Run(() => thread.Join(TimeSpan.FromSeconds(45))), "Desktop flow exceeded 45 seconds.");
        await completion.Task;
    }
    private static async Task Execute(AsyncRelayCommand command, WorkspaceViewModel vm, bool expectError = false)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null);
        var timer = Stopwatch.StartNew();
        while (vm.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
        Assert.False(vm.IsBusy); Assert.Equal(expectError, vm.HasError);
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
