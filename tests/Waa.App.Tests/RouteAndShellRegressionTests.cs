using Waa.App.Data;
using Waa.App.Services;
using Waa.App.ViewModels;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class RouteAndShellRegressionTests
{
    [Fact]
    public async Task ReportRefresh_WhenCurrentManualTaskDisappears_ShowsUnavailableWorkspace()
    {
        using var fixture = new RepositoryFixture();
        var workEntryId = fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.Waiting,
            "Synthetic work that will disappear during refresh.");
        var missingBol = new MissingBolRepository(fixture.DatabasePath);
        missingBol.Initialize();
        var viewModel = new MainViewModel(
            fixture.Repository,
            new ReportUpdateService(
                fixture.Repository,
                missingBol,
                new RollingSevenDayCsvParser(),
                new MissingBolWorkbookParser(),
                () => fixture.Root),
            new RecordingClipboard(),
            missingBolRepository: missingBol);
        await viewModel.InitializeAsync();
        await viewModel.NavigateToDriverAsync("C00003");
        var driverWorkspace = Assert.IsType<DriverWorkspaceViewModel>(viewModel.CurrentWorkspace);
        var attention = Assert.Single(
            driverWorkspace.NeedsAttention,
            item => item.WorkItem?.Record.Id == workEntryId);
        viewModel.OpenAttentionItemCommand.Execute(attention);
        await WaitUntilAsync(() => viewModel.CurrentRoute == WorkspaceRoute.WorkItemTask);

        fixture.ExecuteSql($"DELETE FROM work_entries WHERE id = {workEntryId};");
        viewModel.UpdateReportsCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && !viewModel.Work.IsBusy);

        var unavailable = Assert.IsType<UnavailableWorkspaceViewModel>(viewModel.CurrentWorkspace);
        Assert.Contains("no longer available", unavailable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Back to Driver", unavailable.BackLabel);
    }

    [Fact]
    public void StartupSource_AppliesPersistedLightOrDarkPreference()
    {
        var source = ReadAppFile("App.xaml.cs");

        Assert.Contains(
            "ThemeManager.Apply(themePreferenceStore.GetDarkMode())",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThemePreferenceStore_RoundTripsBothExplicitModes()
    {
        using var fixture = new RepositoryFixture(importDefaultFleet: false);
        var store = new ThemePreferenceStore(fixture.DatabasePath);

        store.SetDarkMode(true);
        Assert.True(store.GetDarkMode());
        store.SetDarkMode(false);
        Assert.False(store.GetDarkMode());
    }

    [Fact]
    public void PersistentStatusArea_IsOutsideCentralWorkspaceContent()
    {
        var source = ReadAppFile("MainWindow.xaml");
        var contentIndex = source.IndexOf("Content=\"{Binding CurrentWorkspace}\"", StringComparison.Ordinal);
        var statusIndex = source.IndexOf("Text=\"{Binding StatusMessage}\"", StringComparison.Ordinal);

        Assert.True(contentIndex >= 0, "MainWindow must contain the central CurrentWorkspace host.");
        Assert.True(statusIndex > contentIndex, "The persistent status area must remain in the shell outside the routed content host.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string ReadAppFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, "src", "Waa.App", .. parts]));
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
}
