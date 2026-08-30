using Waa.App.Data;
using Waa.App.Services;
using Waa.App.ViewModels;
using Xunit;

namespace Waa.App.Tests;

public sealed class WorkLogViewModelTests
{
    [Fact]
    public async Task HandoffEditAndCopy_UseCurrentEditorTextWithoutMutatingWorkHistory()
    {
        using var fixture = new RepositoryFixture();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Synthetic Shift",
            TimeSpan.FromHours(-7),
            "Synthetic Shift",
            "Synthetic Shift");
        var now = new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.FromHours(-7));
        var day = LocalDayRange.Create(now, timeZone);
        var workId = fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            WorkEntryStatus.Done,
            "Synthetic completed work.",
            day.StartUtc.AddHours(2));
        var clipboard = new RecordingClipboard();
        var statuses = new List<string>();
        var viewModel = new HandoffViewModel(
            fixture.Repository,
            new HandoffService(),
            clipboard,
            statuses.Add,
            () => now,
            timeZone);

        await viewModel.RegenerateAsync();
        var generated = viewModel.DraftText;
        viewModel.DraftText += Environment.NewLine + "Edited shift note.";
        await viewModel.CopyAsync();

        Assert.NotEqual(generated, viewModel.DraftText);
        Assert.Equal(viewModel.DraftText, clipboard.Text);
        Assert.Contains("Edited shift note.", clipboard.Text, StringComparison.Ordinal);
        var stored = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(workId));
        Assert.Equal("Synthetic completed work.", stored.Text);
        Assert.Equal(WorkEntryStatus.Done, stored.Status);
        Assert.NotNull(stored.ResolvedUtc);
        Assert.Contains(statuses, status => status.Contains("copied", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Regenerate_IntentionallyReplacesUserEditedDraftFromCurrentRecords()
    {
        using var fixture = new RepositoryFixture();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Synthetic Shift Two",
            TimeSpan.FromHours(-7),
            "Synthetic Shift Two",
            "Synthetic Shift Two");
        var now = new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.FromHours(-7));
        var day = LocalDayRange.Create(now, timeZone);
        fixture.Repository.RecordManualWork(
            fixture.Driver("B00002"),
            WorkEntryStatus.Waiting,
            "Synthetic pending work.",
            day.StartUtc.AddHours(1));
        var viewModel = new HandoffViewModel(
            fixture.Repository,
            new HandoffService(),
            new RecordingClipboard(),
            _ => { },
            () => now,
            timeZone);

        await viewModel.RegenerateAsync();
        var generated = viewModel.DraftText;
        viewModel.DraftText = "Temporary user rewrite.";
        await viewModel.RegenerateAsync();

        Assert.Equal(generated, viewModel.DraftText);
        Assert.Contains("Synthetic pending work.", viewModel.DraftText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewWorkActions_AreDisabledForWhitespaceAndEnabledForTrimmedContent()
    {
        using var fixture = new RepositoryFixture();
        var viewModel = new DriverWorkViewModel(
            fixture.Repository,
            _ => Task.CompletedTask,
            _ => { });
        await viewModel.SetDriverAsync(fixture.Driver("A00001"));

        viewModel.NewWorkText = "   ";
        Assert.False(viewModel.SaveDoneCommand.CanExecute(null));
        Assert.False(viewModel.SaveWaitingCommand.CanExecute(null));
        Assert.False(viewModel.SaveFollowUpCommand.CanExecute(null));

        viewModel.NewWorkText = " Synthetic entry. ";
        Assert.True(viewModel.SaveDoneCommand.CanExecute(null));
        Assert.True(viewModel.SaveWaitingCommand.CanExecute(null));
        Assert.True(viewModel.SaveFollowUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedManualSave_KeepsTypedTextForRetryAndCreatesNoEntry()
    {
        using var fixture = new RepositoryFixture();
        fixture.ExecuteSql("""
            CREATE TRIGGER fail_synthetic_manual_work
            BEFORE INSERT ON work_entries
            WHEN NEW.source = 'Manual'
            BEGIN
                SELECT RAISE(ABORT, 'synthetic manual work failure');
            END;
            """);
        var statusSignal = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new DriverWorkViewModel(
            fixture.Repository,
            _ => Task.CompletedTask,
            message =>
            {
                if (message.Contains("not saved", StringComparison.OrdinalIgnoreCase))
                {
                    statusSignal.TrySetResult(message);
                }
            });
        await viewModel.SetDriverAsync(fixture.Driver("A00001"));
        viewModel.NewWorkText = "Keep this synthetic retry text.";

        viewModel.SaveWaitingCommand.Execute(null);
        var status = await statusSignal.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Contains("not saved", status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Keep this synthetic retry text.", viewModel.NewWorkText);
        Assert.Equal(0, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
    }
}
