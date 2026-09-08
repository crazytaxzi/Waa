using Waa.App.Data;
using Waa.App.Services;
using Waa.App.ViewModels;
using Xunit;

namespace Waa.App.Tests;

public sealed class HandoffViewModelTests
{
    [Fact]
    public async Task RemoveCompletedItem_RegeneratesHandoffAndPreservesSavedHistory()
    {
        using var fixture = new RepositoryFixture();
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var day = LocalDayRange.Create(now, TimeZoneInfo.Utc);
        var id = fixture.Repository.RecordManualWork(
  fixture.Driver("A00001"),
  WorkEntryStatus.Done,
  "Completed item that should leave Handoff.",
  day.StartUtc.AddHours(3));
        var statuses = new List<string>();
        var viewModel = new HandoffViewModel(
  fixture.Repository,
  new HandoffService(),
  new RecordingClipboard(),
  statuses.Add,
  () => now,
  TimeZoneInfo.Utc);

        await viewModel.RegenerateAsync();

        var worked = Assert.Single(viewModel.WorkedItems);
        Assert.Equal(id, worked.WorkEntryId);
        Assert.Contains("Completed item that should leave Handoff.", viewModel.DraftText, StringComparison.Ordinal);

        viewModel.DraftText += Environment.NewLine + "manual draft edit";
        viewModel.DismissWorkedItemCommand.Execute(worked);
        await WaitUntilAsync(() =>
  !viewModel.IsBusy &&
  viewModel.WorkedItems.Count == 0 &&
  !viewModel.DraftText.Contains("Completed item that should leave Handoff.", StringComparison.Ordinal));

        Assert.DoesNotContain("manual draft edit", viewModel.DraftText, StringComparison.Ordinal);
        Assert.NotNull(fixture.Repository.GetWorkEntry(id));
        Assert.Equal(1, fixture.ScalarLong($"SELECT COUNT(*) FROM handoff_dismissals WHERE work_entry_id = {id};"));
        Assert.Contains(statuses, status => status.Contains("Work history was preserved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenWork_IsNotOfferedAsRemovableWorkedItem()
    {
        using var fixture = new RepositoryFixture();
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var day = LocalDayRange.Create(now, TimeZoneInfo.Utc);
        fixture.Repository.RecordManualWork(
  fixture.Driver("B00002"),
  WorkEntryStatus.FollowUp,
  "Follow-up must stay visible.",
  day.StartUtc.AddHours(2));
        var viewModel = new HandoffViewModel(
  fixture.Repository,
  new HandoffService(),
  new RecordingClipboard(),
  _ => { },
  () => now,
  TimeZoneInfo.Utc);

        await viewModel.RegenerateAsync();

        Assert.Empty(viewModel.WorkedItems);
        Assert.Contains("Follow-up must stay visible.", viewModel.DraftText, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
