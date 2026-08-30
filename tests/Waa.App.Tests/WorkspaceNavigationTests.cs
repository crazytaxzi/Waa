using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;
using Waa.App.ViewModels;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class WorkspaceNavigationTests
{
    private static readonly DateTimeOffset ImportUtc =
        new(2026, 8, 30, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Navigator_StartsOnFleetQueue()
    {
        var navigator = new WorkspaceNavigator();

        Assert.Equal(WorkspaceLocation.FleetQueue, navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Navigator_BackReturnsToActualPriorRoute()
    {
        var navigator = new WorkspaceNavigator();
        var driver = new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, "A00001");
        var task = new WorkspaceLocation(WorkspaceRoute.IdleTask, "A00001");
        navigator.Navigate(driver);
        navigator.Navigate(task);

        Assert.Equal(driver, navigator.Back());
        Assert.Equal(WorkspaceLocation.FleetQueue, navigator.Back());
    }

    [Fact]
    public void Navigator_ReplacePreservesExistingBackStack()
    {
        var navigator = new WorkspaceNavigator();
        navigator.Navigate(new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, "A00001"));
        navigator.Replace(new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, "A00001", Focus: DriverWorkspaceFocus.OpenWork));

        Assert.True(navigator.CanGoBack);
        Assert.Equal(WorkspaceLocation.FleetQueue, navigator.Back());
    }

    [Fact]
    public void Navigator_ResetReturnsHomeAndClearsHistory()
    {
        var navigator = new WorkspaceNavigator();
        navigator.Navigate(new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, "A00001"));
        navigator.Navigate(new WorkspaceLocation(WorkspaceRoute.NewWork, "A00001"));

        navigator.Reset();

        Assert.Equal(WorkspaceLocation.FleetQueue, navigator.Current);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void DriverRouteIdentity_UsesDurableDriverCodeNotUnitCode()
    {
        var route = new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, "A00001");
        var oldRecord = WorkEntryTestData.FleetDriver("A00001", "Alex Example", 62m, null, 0) with
        {
            UnitCode = "270101"
        };
        var newRecord = oldRecord with { UnitCode = "999999" };

        Assert.Equal("A00001", route.DriverCode);
        Assert.NotEqual(oldRecord.UnitCode, newRecord.UnitCode);
        Assert.Equal(oldRecord.DriverCode, newRecord.DriverCode);
    }

    [Fact]
    public void MainViewModel_DefaultWorkspaceIsFleetQueue()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);

        Assert.Equal(WorkspaceRoute.FleetQueue, environment.ViewModel.CurrentRoute);
        Assert.Equal("Fleet", environment.ViewModel.BreadcrumbText);
    }

    [Fact]
    public async Task Initialization_LeavesFleetQueueAsFreshLaunchHome()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);

        await environment.ViewModel.InitializeAsync();

        Assert.Equal(WorkspaceRoute.FleetQueue, environment.ViewModel.CurrentRoute);
        Assert.NotEmpty(environment.ViewModel.Drivers);
    }

    [Fact]
    public async Task OpeningDriver_NavigatesByDriverCodeAndBuildsBreadcrumb()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);

        await environment.ViewModel.NavigateToDriverAsync("A00001");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal("A00001", workspace.Driver.DriverCode);
        Assert.Equal("Fleet > Alex Example", workspace.Breadcrumb);
    }

    [Fact]
    public async Task BackFromDriver_ReturnsToFleetQueue()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("A00001");

        await environment.ViewModel.NavigateBackAsync();

        Assert.Equal(WorkspaceRoute.FleetQueue, environment.ViewModel.CurrentRoute);
    }

    [Fact]
    public async Task QueueSearchAndSelection_SurviveDriverRoundTrip()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        environment.ViewModel.SearchText = "Alex";
        var selected = Assert.Single(environment.ViewModel.Drivers);
        environment.ViewModel.SelectedDriver = selected;
        await WaitUntilAsync(() => !environment.ViewModel.Work.IsBusy);

        await environment.ViewModel.NavigateToDriverAsync(selected.DriverCode);
        await environment.ViewModel.NavigateBackAsync();

        Assert.Equal("Alex", environment.ViewModel.SearchText);
        Assert.Equal("A00001", environment.ViewModel.SelectedDriver?.DriverCode);
        Assert.Single(environment.ViewModel.Drivers);
    }

    [Fact]
    public async Task Handoff_OpensInCentralHostAndDraftSurvivesNavigation()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);

        environment.ViewModel.OpenHandoffCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.Handoff && !environment.ViewModel.Handoff.IsBusy);
        environment.ViewModel.Handoff.DraftText = "Edited handoff draft";
        environment.ViewModel.BackToQueueCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.FleetQueue);
        environment.ViewModel.OpenHandoffCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.Handoff);

        Assert.Equal("Edited handoff draft", environment.ViewModel.Handoff.DraftText);
        Assert.IsType<HandoffWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
    }

    [Fact]
    public async Task UnmatchedBol_OpensInCentralReadOnlyWorkspace()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);
        ImportBol(environment.MissingBolRepository, "HASH-UNMATCHED-WORKSPACE", BolItem("BOL-U1", "UNKNOWN"));
        await environment.ViewModel.InitializeAsync();

        environment.ViewModel.OpenUnmatchedBolCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.UnmatchedBol);

        var workspace = Assert.IsType<UnmatchedBolWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Single(workspace.Items);
        Assert.Contains("exact", workspace.Items[0].ExactMatchExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriverWithNoOpenWork_ShowsProfessionalEmptyState()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);

        await environment.ViewModel.NavigateToDriverAsync("C00003");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.False(workspace.HasNeedsAttention);
        Assert.Equal("No work currently needs attention for this driver.", workspace.EmptyStateText);
    }

    [Fact]
    public async Task DriverWorkspace_ShowsIdleLinkedWorkExactlyOnce()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordIdleContact(
            fixture.Driver("A00001"),
            IdleContactOutcome.Attempted,
            "Attempted contact",
            50m);
        var environment = await CreateInitializedEnvironmentAsync(fixture);

        await environment.ViewModel.NavigateToDriverAsync("A00001");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Single(workspace.NeedsAttention, item => item.Kind == DriverAttentionKind.Idle);
        Assert.DoesNotContain(
            workspace.NeedsAttention,
            item => item.Kind == DriverAttentionKind.ManualWork &&
                    item.WorkItem?.Record.Source == WorkEntrySource.IdleContact);
    }

    [Fact]
    public async Task DriverWorkspace_ShowsBolTaskExactlyOnceWithoutLinkedWorkDuplicate()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);
        ImportBol(environment.MissingBolRepository, "HASH-BOL-ONCE", BolItem("BOL-100", "A00001"));
        await environment.ViewModel.InitializeAsync();

        await environment.ViewModel.NavigateToDriverAsync("A00001");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Single(workspace.NeedsAttention, item => item.Kind == DriverAttentionKind.MissingBol);
        Assert.DoesNotContain(
            workspace.NeedsAttention,
            item => item.Kind == DriverAttentionKind.ManualWork &&
                    item.WorkItem?.Record.Source == WorkEntrySource.MissingBolTask);
    }

    [Fact]
    public async Task DriverWorkspace_ShowsEachManualOpenItemOnce()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(fixture.Driver("C00003"), WorkEntryStatus.Waiting, "Waiting on ETA");
        fixture.Repository.RecordManualWork(fixture.Driver("C00003"), WorkEntryStatus.FollowUp, "Call terminal");
        var environment = await CreateInitializedEnvironmentAsync(fixture);

        await environment.ViewModel.NavigateToDriverAsync("C00003");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal(2, workspace.NeedsAttention.Count(item => item.Kind == DriverAttentionKind.ManualWork));
        Assert.Equal(2, workspace.NeedsAttention.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ClickingIdleAttention_OpensIdleTaskAndBackReturnsSameDriver()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("A00001");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        var idle = driverWorkspace.NeedsAttention.Single(item => item.Kind == DriverAttentionKind.Idle);

        environment.ViewModel.OpenAttentionItemCommand.Execute(idle);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.IdleTask);
        Assert.IsType<IdleTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        await environment.ViewModel.NavigateBackAsync();

        var returned = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal("A00001", returned.Driver.DriverCode);
    }

    [Fact]
    public async Task ClickingBolAttention_OpensCorrectOrderTask()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);
        ImportBol(
            environment.MissingBolRepository,
            "HASH-BOL-OPEN",
            BolItem("BOL-OLD", "A00001", new DateOnly(2026, 8, 25)),
            BolItem("BOL-NEW", "A00001", new DateOnly(2026, 8, 28)));
        await environment.ViewModel.InitializeAsync();
        await environment.ViewModel.NavigateToDriverAsync("A00001");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        var bol = driverWorkspace.NeedsAttention.Single(item => item.Title == "Order BOL-OLD");

        environment.ViewModel.OpenAttentionItemCommand.Execute(bol);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.MissingBolTask);

        var task = Assert.IsType<MissingBolTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal("BOL-OLD", task.Item.OrderNumber);
        Assert.Equal("Fleet > Alex Example > Missing BOL > BOL-OLD", task.Breadcrumb);
    }

    [Fact]
    public async Task ClickingManualAttention_OpensCorrectWorkItem()
    {
        using var fixture = new RepositoryFixture();
        var id = fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.Waiting,
            "Waiting on updated ETA");
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("C00003");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        var work = driverWorkspace.NeedsAttention.Single(item => item.Kind == DriverAttentionKind.ManualWork);

        environment.ViewModel.OpenAttentionItemCommand.Execute(work);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.WorkItemTask);

        var task = Assert.IsType<WorkItemTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal(id, task.Item.Record.Id);
    }

    [Fact]
    public async Task AddWork_OpensFocusedNewWorkWorkspace()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("C00003");

        environment.ViewModel.OpenNewWorkCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.NewWork);

        var workspace = Assert.IsType<NewWorkWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal("C00003", workspace.Driver.DriverCode);
    }

    [Fact]
    public async Task SuccessfulNewWork_ReturnsToDriverAndHighlightsSavedEntry()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("C00003");
        environment.ViewModel.OpenNewWorkCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.NewWork);
        environment.ViewModel.Work.NewWorkText = "Waiting on customer response";

        environment.ViewModel.Work.SaveWaitingCommand.Execute(null);
        await WaitUntilAsync(() =>
            environment.ViewModel.CurrentRoute == WorkspaceRoute.DriverWorkspace &&
            environment.ViewModel.Work.LastSavedWorkEntryId is not null &&
            !environment.ViewModel.Work.IsBusy);

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal(environment.ViewModel.Work.LastSavedWorkEntryId, workspace.HighlightedWorkEntryId);
        Assert.Contains(workspace.NeedsAttention, item => item.WorkItem?.Record.Id == workspace.HighlightedWorkEntryId);
    }

    [Fact]
    public async Task ReportRefresh_PreservesValidDriverRouteAndManualDraft()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("C00003");
        environment.ViewModel.OpenNewWorkCommand.Execute(null);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.NewWork);
        environment.ViewModel.Work.NewWorkText = "Unsaved local draft";

        environment.ViewModel.UpdateReportsCommand.Execute(null);
        await WaitUntilAsync(() => !environment.ViewModel.IsBusy && !environment.ViewModel.Work.IsBusy);

        Assert.Equal(WorkspaceRoute.NewWork, environment.ViewModel.CurrentRoute);
        Assert.Equal("Unsaved local draft", environment.ViewModel.Work.NewWorkText);
        Assert.Equal("C00003", environment.ViewModel.SelectedDriver?.DriverCode);
    }

    [Fact]
    public async Task MissingBolNote_SurvivesRefreshWithoutSave()
    {
        using var fixture = new RepositoryFixture();
        var environment = CreateEnvironment(fixture);
        ImportBol(environment.MissingBolRepository, "HASH-BOL-DRAFT", BolItem("BOL-DRAFT", "A00001"));
        await environment.ViewModel.InitializeAsync();
        await environment.ViewModel.NavigateToDriverAsync("A00001");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        var bol = driverWorkspace.NeedsAttention.Single(item => item.Kind == DriverAttentionKind.MissingBol);
        environment.ViewModel.OpenAttentionItemCommand.Execute(bol);
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.MissingBolTask);
        var task = Assert.IsType<MissingBolTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        task.Item.Note = "Do not discard this note";

        environment.ViewModel.UpdateReportsCommand.Execute(null);
        await WaitUntilAsync(() => !environment.ViewModel.IsBusy && !environment.ViewModel.MissingBol!.IsBusy);

        var refreshed = Assert.IsType<MissingBolTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Equal("Do not discard this note", refreshed.Item.Note);
    }

    [Fact]
    public async Task NextWorkItem_FollowsIdleThenOldestBolThenManualFollowUpThenWaiting()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(fixture.Driver("A00001"), WorkEntryStatus.Waiting, "Waiting item");
        fixture.Repository.RecordManualWork(fixture.Driver("A00001"), WorkEntryStatus.FollowUp, "Follow-up item");
        var environment = CreateEnvironment(fixture);
        ImportBol(
            environment.MissingBolRepository,
            "HASH-ORDERING",
            BolItem("BOL-2", "A00001", new DateOnly(2026, 8, 28)),
            BolItem("BOL-1", "A00001", new DateOnly(2026, 8, 26)));
        await environment.ViewModel.InitializeAsync();
        await environment.ViewModel.NavigateToDriverAsync("A00001");

        var workspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);

        Assert.Equal(
            new[]
            {
                DriverAttentionKind.Idle,
                DriverAttentionKind.MissingBol,
                DriverAttentionKind.MissingBol,
                DriverAttentionKind.ManualWork,
                DriverAttentionKind.ManualWork
            },
            workspace.NeedsAttention.Select(item => item.Kind));
        Assert.Equal("Order BOL-1", workspace.NeedsAttention[1].Title);
        Assert.Equal("Follow-up", workspace.NeedsAttention[3].StatusText);
        Assert.Equal("Waiting", workspace.NeedsAttention[4].StatusText);
    }

    [Fact]
    public async Task ResolveRemovesManualItemFromNeedsAttentionAndReopenRestoresIt()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(fixture.Driver("C00003"), WorkEntryStatus.Waiting, "Resolve and reopen me");
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("C00003");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        environment.ViewModel.OpenAttentionItemCommand.Execute(
            driverWorkspace.NeedsAttention.Single(item => item.Kind == DriverAttentionKind.ManualWork));
        await WaitUntilAsync(() => environment.ViewModel.CurrentRoute == WorkspaceRoute.WorkItemTask);
        var task = Assert.IsType<WorkItemTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);

        task.Item.ResolveCommand.Execute(null);
        await WaitUntilAsync(() =>
            environment.ViewModel.CurrentWorkspace is WorkItemTaskWorkspaceViewModel current &&
            current.Item.IsResolved &&
            !environment.ViewModel.Work.IsBusy);
        var resolvedTask = Assert.IsType<WorkItemTaskWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        resolvedTask.Item.ReopenCommand.Execute(null);
        await WaitUntilAsync(() =>
            environment.ViewModel.CurrentWorkspace is WorkItemTaskWorkspaceViewModel current &&
            !current.Item.IsResolved &&
            !environment.ViewModel.Work.IsBusy);
        await environment.ViewModel.NavigateBackAsync();

        var returned = Assert.IsType<DriverWorkspaceViewModel>(environment.ViewModel.CurrentWorkspace);
        Assert.Contains(returned.NeedsAttention, item => item.Kind == DriverAttentionKind.ManualWork);
    }

    [Fact]
    public async Task ThemeChange_DoesNotResetCurrentNavigationRoute()
    {
        using var fixture = new RepositoryFixture();
        var environment = await CreateInitializedEnvironmentAsync(fixture);
        await environment.ViewModel.NavigateToDriverAsync("A00001");

        ThemeManager.Apply(darkMode: true);
        ThemeManager.Apply(darkMode: false);

        Assert.Equal(WorkspaceRoute.DriverWorkspace, environment.ViewModel.CurrentRoute);
        Assert.Equal("A00001", environment.ViewModel.SelectedDriver?.DriverCode);
    }

    [Fact]
    public void ActivityDetailView_IsReadOnlyBySourceContract()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Waa.App", "Views", "ActivityDetailView.xaml"));

        Assert.DoesNotContain("Command=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox", source, StringComparison.Ordinal);
        Assert.Contains("read-only", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshMainViewModel_DoesNotPersistDeepRouteAcrossRestart()
    {
        using var fixture = new RepositoryFixture();
        var first = CreateEnvironment(fixture);
        var second = CreateEnvironment(fixture);

        Assert.Equal(WorkspaceRoute.FleetQueue, first.ViewModel.CurrentRoute);
        Assert.Equal(WorkspaceRoute.FleetQueue, second.ViewModel.CurrentRoute);
    }

    private static WorkspaceTestEnvironment CreateEnvironment(RepositoryFixture fixture)
    {
        var missingBol = new MissingBolRepository(fixture.DatabasePath);
        missingBol.Initialize();
        var updateService = new ReportUpdateService(
            fixture.Repository,
            missingBol,
            new RollingSevenDayCsvParser(),
            new MissingBolWorkbookParser(),
            () => fixture.Root);
        var viewModel = new MainViewModel(
            fixture.Repository,
            updateService,
            new RecordingClipboard(),
            missingBolRepository: missingBol);
        return new WorkspaceTestEnvironment(viewModel, missingBol);
    }

    private static async Task<WorkspaceTestEnvironment> CreateInitializedEnvironmentAsync(RepositoryFixture fixture)
    {
        var environment = CreateEnvironment(fixture);
        await environment.ViewModel.InitializeAsync();
        return environment;
    }

    private static MissingBolImportResult ImportBol(
        MissingBolRepository repository,
        string hash,
        params MissingBolSourceItem[] items) =>
        repository.ImportWorkbook(
            new MissingBolWorkbookImport("Synthetic Sheet", items),
            "Order Details Missing BOL-workspace.xlsx",
            @"C:\Synthetic\Order Details Missing BOL-workspace.xlsx",
            hash,
            ImportUtc.UtcDateTime,
            ImportUtc);

    private static MissingBolSourceItem BolItem(
        string orderNumber,
        string driverCode,
        DateOnly? date = null) =>
        new(
            MissingBolText.NormalizeExact(orderNumber),
            orderNumber,
            $"TMEX-{orderNumber}",
            $"LOG-{orderNumber}",
            "Synthetic Customer",
            "0611",
            date ?? new DateOnly(2026, 8, 27),
            "Boise, ID",
            "Auburn, WA",
            "Linehaul",
            "Synthetic Terminal",
            "LEAD-BOL",
            "Active",
            driverCode.Trim(),
            MissingBolText.NormalizeExact(driverCode),
            "Synthetic Source Driver",
            125m,
            130m,
            2);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Waa.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Waa.sln from the test output directory.");
    }

    private sealed record WorkspaceTestEnvironment(
        MainViewModel ViewModel,
        MissingBolRepository MissingBolRepository);
}
