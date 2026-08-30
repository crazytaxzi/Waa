using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using Waa.App.Data;
using Waa.App.Services;
using Waa.App.ViewModels;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class MissingBolIntegrationTests
{
    [Fact]
    public async Task ReportUpdate_AllowsRollingSuccessWhenMissingBolFails()
    {
        using var fixture = new RepositoryFixture(importDefaultFleet: false);
        var missingBolRepository = CreateMissingBolRepository(fixture);
        File.WriteAllText(
            Path.Combine(fixture.Root, "rolling 7 day_data-synthetic.csv"),
            BuildRollingCsv(),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(fixture.Root, "Order Details Missing BOL-bad.xlsx"),
            "not an xlsx workbook",
            Encoding.UTF8);
        var service = CreateUpdateService(fixture, missingBolRepository);

        var result = await service.UpdateAsync();

        Assert.Equal(ReportSourceUpdateState.Imported, result.RollingSevenDay.State);
        Assert.Equal(ReportSourceUpdateState.Failed, result.MissingBol.State);
        Assert.Contains("Partial update", result.Message, StringComparison.Ordinal);
        Assert.Single(fixture.Repository.LoadFleet().Drivers);
        Assert.Empty(missingBolRepository.LoadFleetState().UnmatchedItems);
    }

    [Fact]
    public async Task ReportUpdate_AllowsMissingBolSuccessWithoutRollingAndMissingLaterPreservesState()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        var workbookPath = Path.Combine(fixture.Root, "Order Details Missing BOL-synthetic.xlsx");
        File.WriteAllBytes(
            workbookPath,
            BuildMissingBolWorkbook(("SYN3001", "A00001", "Alex Source Name", "8/27/2026")));
        var service = CreateUpdateService(fixture, missingBolRepository);

        var imported = await service.UpdateAsync();

        Assert.Equal(ReportSourceUpdateState.NotFound, imported.RollingSevenDay.State);
        Assert.Equal(ReportSourceUpdateState.Imported, imported.MissingBol.State);
        Assert.Equal(1, missingBolRepository.LoadFleetState().OpenMatchedCount);
        Assert.NotNull(missingBolRepository.GetItemByOrder("SYN3001"));

        File.Delete(workbookPath);
        var missing = await service.UpdateAsync();

        Assert.Equal(ReportSourceUpdateState.NotFound, missing.MissingBol.State);
        Assert.NotNull(missingBolRepository.GetItemByOrder("SYN3001"));
        Assert.Equal(1, missingBolRepository.LoadFleetState().OpenMatchedCount);
    }

    [Fact]
    public async Task ReportUpdate_BadNewerWorkbookPreservesAcceptedStateAndUsesOlderAcceptedHash()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        var validPath = Path.Combine(fixture.Root, "Order Details Missing BOL-synthetic.xlsx");
        File.WriteAllBytes(
            validPath,
            BuildMissingBolWorkbook(("SYN3002", "A00001", "Alex Source Name", "8/27/2026")));
        File.SetLastWriteTimeUtc(validPath, new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc));
        var service = CreateUpdateService(fixture, missingBolRepository);
        var first = await service.UpdateAsync();
        Assert.Equal(ReportSourceUpdateState.Imported, first.MissingBol.State);

        var badPath = Path.Combine(fixture.Root, "Order Details Missing BOL-new.xlsx");
        File.WriteAllText(badPath, "partial download", Encoding.UTF8);
        File.SetLastWriteTimeUtc(badPath, new DateTime(2026, 8, 30, 11, 0, 0, DateTimeKind.Utc));

        var second = await service.UpdateAsync();

        Assert.Equal(ReportSourceUpdateState.Current, second.MissingBol.State);
        Assert.Contains("ignored 1 newer invalid candidate", second.MissingBol.Message, StringComparison.Ordinal);
        Assert.NotNull(missingBolRepository.GetItemByOrder("SYN3002"));
        Assert.Equal(1, missingBolRepository.LoadFleetState().OpenMatchedCount);
    }

    [Fact]
    public async Task MainViewModel_OrderSearchNextAndSelectedDriverIncludeBolOnlyWork()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        ImportMissingBol(
            missingBolRepository,
            "HASH-QUEUE-INTEGRATION",
            Item("SYNQ001", "C00003", new DateOnly(2026, 8, 27)),
            Item("SYNQ002", "C00003", new DateOnly(2026, 8, 28)));
        fixture.Repository.RecordIdleContact(
            fixture.Driver("A00001"),
            IdleContactOutcome.Spoke,
            null,
            50m);
        fixture.Repository.RecordIdleContact(
            fixture.Driver("B00002"),
            IdleContactOutcome.Spoke,
            null,
            50m);
        var viewModel = new MainViewModel(
            fixture.Repository,
            CreateUpdateService(fixture, missingBolRepository),
            new RecordingClipboard(),
            missingBolRepository: missingBolRepository);

        await viewModel.InitializeAsync();
        viewModel.SearchText = "SYNQ002";

        var searchResult = Assert.Single(viewModel.Drivers);
        Assert.Equal("C00003", searchResult.DriverCode);
        Assert.Equal(2, searchResult.MissingBolCount);

        viewModel.SearchText = string.Empty;
        viewModel.SelectedDriver = viewModel.Drivers.Single(driver => driver.DriverCode == "D00004");
        await WaitUntilAsync(() => !viewModel.Work.IsBusy && viewModel.MissingBol?.IsBusy != true);
        viewModel.NextNeedingAttentionCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.SelectedDriver?.DriverCode == "C00003");
        await WaitUntilAsync(() => viewModel.MissingBol?.Items.Count == 2);

        Assert.Equal("C00003", viewModel.SelectedDriver?.DriverCode);
        Assert.Equal(2, viewModel.MissingBol?.Items.Count);
        Assert.All(viewModel.MissingBol!.Items, item => Assert.StartsWith("SYNQ00", item.OrderNumber));
    }

    [Fact]
    public void Handoff_MissingBolLifecycleAppearsOnceAndReturnsAfterReopen()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        var timeZone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var day = LocalDayRange.Create(now, timeZone);
        ImportMissingBol(
            missingBolRepository,
            "HASH-HANDOFF-INTEGRATION",
            day.StartUtc.AddHours(1),
            Item("SYNH001", "A00001", new DateOnly(2026, 8, 27)));
        var item = Assert.IsType<MissingBolItemRecord>(
            missingBolRepository.GetItemByOrder("SYNH001"));
        missingBolRepository.RecordAction(
            item.Id,
            MissingBolActionOutcome.Requested,
            null,
            day.StartUtc.AddHours(2));

        var requested = GenerateHandoff(fixture.Repository, missingBolRepository, day);

        Assert.Equal(1, requested.NeedsFollowUpCount);
        Assert.Equal(1, requested.CompletedTodayCount);
        Assert.Equal(1, CountOccurrences(requested.Text, "Status: Requested."));
        Assert.Equal(1, CountOccurrences(requested.Text, "Requested missing BOL for order SYNH001."));
        Assert.Contains("Boise, ID → Auburn, WA", requested.Text, StringComparison.Ordinal);
        Assert.Contains("270101 — Alex Example [A00001]", requested.Text, StringComparison.Ordinal);

        missingBolRepository.RecordAction(
            item.Id,
            MissingBolActionOutcome.Resolved,
            null,
            day.StartUtc.AddHours(3));
        var resolved = GenerateHandoff(fixture.Repository, missingBolRepository, day);

        Assert.Equal(0, resolved.NeedsFollowUpCount);
        Assert.Equal(2, resolved.CompletedTodayCount);
        Assert.Contains("Resolved missing BOL for order SYNH001.", resolved.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: Resolved.", resolved.Text, StringComparison.Ordinal);

        missingBolRepository.RecordAction(
            item.Id,
            MissingBolActionOutcome.Reopen,
            null,
            day.StartUtc.AddHours(4));
        var reopened = GenerateHandoff(fixture.Repository, missingBolRepository, day);

        Assert.Equal(1, reopened.NeedsFollowUpCount);
        Assert.Equal(3, reopened.CompletedTodayCount);
        Assert.Equal(1, CountOccurrences(reopened.Text, "Status: Open."));
        Assert.Contains("Reopened missing BOL for order SYNH001.", reopened.Text, StringComparison.Ordinal);
        var current = Assert.IsType<MissingBolItemRecord>(
            missingBolRepository.GetItemByOrder("SYNH001"));
        var task = Assert.IsType<WorkEntryRecord>(
            fixture.Repository.GetWorkEntry(current.TaskWorkEntryId!.Value));
        Assert.Equal("270101", task.UnitCodeSnapshot);
        Assert.Null(task.ResolvedUtc);
    }

    [Fact]
    public async Task MissingBolItemViewModel_PreventsDuplicateSubmitAndRetainsFailedNote()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        ImportMissingBol(
            missingBolRepository,
            "HASH-VM-INTEGRATION",
            Item("SYNV001", "A00001", new DateOnly(2026, 8, 27)));
        var record = Assert.IsType<MissingBolItemRecord>(
            missingBolRepository.GetItemByOrder("SYNV001"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var viewModel = new MissingBolItemViewModel(
            record,
            async (_, _, _) =>
            {
                Interlocked.Increment(ref callCount);
                entered.TrySetResult();
                await release.Task;
                return false;
            });
        viewModel.Note = "Keep this note for retry.";

        viewModel.RequestedCommand.Execute(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(viewModel.RequestedCommand.CanExecute(null));
        viewModel.RequestedCommand.Execute(null);
        release.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, callCount);
        Assert.Equal("Keep this note for retry.", viewModel.Note);

        var reopenedRecord = record with
        {
            CurrentStatus = MissingBolStatus.Open,
            ResolvedUtc = null,
            ReturnedAfterResolution = true,
            IsPresentInLatestImport = true
        };
        var reopenedViewModel = new MissingBolItemViewModel(
            reopenedRecord,
            (_, _, _) => Task.FromResult(true));
        Assert.Equal(string.Empty, reopenedViewModel.PresenceWarning);
    }

    [Fact]
    public void ReportUpdateService_HasNoWatcherOrRecurringTimerState()
    {
        var forbiddenFields = typeof(ReportUpdateService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field =>
                typeof(FileSystemWatcher).IsAssignableFrom(field.FieldType) ||
                field.FieldType.FullName is "System.Threading.Timer" or "System.Timers.Timer")
            .ToArray();

        Assert.Empty(forbiddenFields);
    }

    private static MissingBolRepository CreateMissingBolRepository(RepositoryFixture fixture)
    {
        var repository = new MissingBolRepository(fixture.DatabasePath);
        repository.Initialize();
        return repository;
    }

    private static ReportUpdateService CreateUpdateService(
        RepositoryFixture fixture,
        MissingBolRepository missingBolRepository) =>
        new(
            fixture.Repository,
            missingBolRepository,
            new RollingSevenDayCsvParser(),
            new MissingBolWorkbookParser(),
            () => fixture.Root);

    private static MissingBolImportResult ImportMissingBol(
        MissingBolRepository repository,
        string hash,
        params MissingBolSourceItem[] items) =>
        ImportMissingBol(
            repository,
            hash,
            new DateTimeOffset(2026, 8, 30, 15, 0, 0, TimeSpan.Zero),
            items);

    private static MissingBolImportResult ImportMissingBol(
        MissingBolRepository repository,
        string hash,
        DateTimeOffset importedUtc,
        params MissingBolSourceItem[] items) =>
        repository.ImportWorkbook(
            new MissingBolWorkbookImport("Synthetic Sheet", items),
            "Order Details Missing BOL-synthetic.xlsx",
            @"C:\Synthetic\Order Details Missing BOL-synthetic.xlsx",
            hash,
            importedUtc.UtcDateTime,
            importedUtc);

    private static MissingBolSourceItem Item(
        string orderNumber,
        string driverCode,
        DateOnly date) =>
        new(
            MissingBolText.NormalizeExact(orderNumber),
            orderNumber,
            $"TMEX-{orderNumber}",
            $"LOG-{orderNumber}",
            "Synthetic Customer",
            "0611",
            date,
            "Boise, ID",
            "Auburn, WA",
            "Linehaul",
            "Synthetic Terminal",
            "LEAD-BOL",
            "Active",
            driverCode,
            MissingBolText.NormalizeExact(driverCode),
            "Synthetic Source Name",
            125m,
            130m,
            2);

    private static HandoffResult GenerateHandoff(
        WaaRepository workRepository,
        MissingBolRepository missingBolRepository,
        LocalDayRange day)
    {
        var entries = workRepository.LoadHandoffEntries(day.StartUtc, day.EndUtc);
        return new HandoffService().Generate(
            missingBolRepository.ApplyWorkSources(entries),
            day);
    }

    private static string BuildRollingCsv()
    {
        const string header =
            "Group by (copy),Measure Names,Week Start Date,[Rolling 7 Day Engine Time]/60," +
            "[Rolling 7 Day Idle Time]/60,Rolling 7 Day Dispatch Miles,Rolling 7 Day Qualcomm Miles," +
            "Cost Center,Driver Leader,Driver Terminal,Fleet Leader,OPS LOB,Rolling 7 Day Start Date," +
            "Unit Code,Week Start Date,Measure Values";
        var rows = new List<string> { header };
        foreach (var week in new[] { "8/30/2026", "8/23/2026", "8/16/2026", "8/9/2026" })
        {
            rows.Add($"A00001 Alex Example,OOR %,{week},100,40,1000,1010,611 - Synthetic,LEAD000001,Synthetic,TEST,Line Haul,{week},270101,{week},0.4");
            rows.Add($"A00001 Alex Example,Idle %,{week},100,40,1000,1010,611 - Synthetic,LEAD000001,Synthetic,TEST,Line Haul,{week},270101,{week},0.4");
        }

        return string.Join("\r\n", rows);
    }

    private static byte[] BuildMissingBolWorkbook(
        params (string Order, string DriverCode, string DriverName, string EmptyCallDate)[] rows)
    {
        var headers = new[]
        {
            "Order #",
            "TMEX Order #",
            "Logistics Order#",
            "Bill To",
            "Division#",
            "Empty Call Date",
            "Origin City St",
            "Destination City St",
            "Rev Type",
            "Terminal",
            "Driver Leader",
            "Driver Status",
            "Last Dispatch Driver cd",
            "Last Dispatch Driver nm",
            "Loaded Miles",
            "Order Level Order Miles"
        };
        var worksheetRows = new List<string>
        {
            BuildInlineRow(1, headers)
        };
        for (var index = 0; index < rows.Length; index++)
        {
            var source = rows[index];
            worksheetRows.Add(BuildInlineRow(
                index + 2,
                new[]
                {
                    source.Order,
                    $"TMEX-{source.Order}",
                    $"LOG-{source.Order}",
                    "Synthetic Customer",
                    "0611",
                    source.EmptyCallDate,
                    "Boise, ID",
                    "Auburn, WA",
                    "Linehaul",
                    "Synthetic Terminal",
                    "LEAD-BOL",
                    "Active",
                    source.DriverCode,
                    source.DriverName,
                    "125",
                    "130"
                }));
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(
                archive,
                "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
            WriteZipEntry(
                archive,
                "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            WriteZipEntry(
                archive,
                "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Synthetic Sheet\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteZipEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");
            WriteZipEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                string.Concat(worksheetRows) +
                "</sheetData></worksheet>");
        }

        return output.ToArray();
    }

    private static string BuildInlineRow(int rowNumber, IReadOnlyList<string> values)
    {
        var cells = values.Select((value, column) =>
            $"<c r=\"{ColumnName(column)}{rowNumber.ToString(CultureInfo.InvariantCulture)}\" t=\"inlineStr\">" +
            $"<is><t>{EscapeXml(value)}</t></is></c>");
        return $"<row r=\"{rowNumber.ToString(CultureInfo.InvariantCulture)}\">{string.Concat(cells)}</row>";
    }

    private static string ColumnName(int zeroBasedColumn)
    {
        var value = zeroBasedColumn + 1;
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static void WriteZipEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
