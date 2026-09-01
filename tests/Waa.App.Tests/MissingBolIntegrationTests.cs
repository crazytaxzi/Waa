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
        Assert.False(missingBolRepository.HasCurrentSnapshot);
    }

    [Fact]
    public async Task MissingBolWorkbook_LoadsCurrentViewAndDeletingFileClearsIt()
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
        Assert.Contains("read-only, not stored", imported.MissingBol.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, missingBolRepository.LoadFleetState().OpenMatchedCount);
        Assert.NotNull(missingBolRepository.GetItemByOrder("SYN3001"));
        Assert.Equal(0, fixture.Driver("A00001").OpenWorkCount);

        File.Delete(workbookPath);
        var missing = await service.UpdateAsync();

        Assert.Equal(ReportSourceUpdateState.NotFound, missing.MissingBol.State);
        Assert.Null(missingBolRepository.GetItemByOrder("SYN3001"));
        Assert.Equal(0, missingBolRepository.LoadFleetState().OpenMatchedCount);
    }

    [Fact]
    public async Task BadNewerWorkbook_CanFallBackToOlderValidCurrentWorkbook()
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
    }

    [Fact]
    public async Task CurrentBolRows_AppearInFleetSearchWithoutCreatingOpenWork()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "Order Details Missing BOL-synthetic.xlsx"),
            BuildMissingBolWorkbook(
                ("SYNQ001", "C00003", "Casey Source", "8/27/2026"),
                ("SYNQ002", "C00003", "Casey Source", "8/28/2026")));
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
        Assert.Equal(0, searchResult.OpenWorkCount);
        Assert.False(searchResult.HasOpenWork);
        Assert.Equal(0, fixture.Driver("C00003").OpenWorkCount);
    }

    [Fact]
    public async Task DriverBolDetail_IsReadOnlyCurrentWorkbookData()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "Order Details Missing BOL-synthetic.xlsx"),
            BuildMissingBolWorkbook(("SYNR001", "A00001", "Different Source Name", "8/27/2026")));
        var viewModel = new MainViewModel(
            fixture.Repository,
            CreateUpdateService(fixture, missingBolRepository),
            new RecordingClipboard(),
            missingBolRepository: missingBolRepository);

        await viewModel.InitializeAsync();
        await viewModel.NavigateToDriverAsync("A00001", DriverWorkspaceFocus.MissingBol);
        await WaitUntilAsync(() => viewModel.MissingBol?.Items.Count == 1);

        var item = Assert.Single(viewModel.MissingBol!.Items);
        Assert.Equal("In current report", item.StatusDisplay);
        Assert.Equal("Current workbook", item.PresenceDisplay);
        Assert.True(item.HasNameWarning);
        Assert.True(item.IsResolved); // internal compatibility flag keeps report rows out of actionable-work builders
        Assert.Equal(DriverAttentionKind.MissingBol, item.AttentionItem.Kind);
    }

    [Fact]
    public void Handoff_UsesCurrentWorkbookBolRowsWithoutPersistingThem()
    {
        using var fixture = new RepositoryFixture();
        var missingBolRepository = CreateMissingBolRepository(fixture);
        ImportMissingBol(
            missingBolRepository,
            "HASH-HANDOFF",
            Item("SYNH001", "A00001", new DateOnly(2026, 8, 27)));
        var fleet = fixture.Repository.LoadFleet();
        var before = fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;");
        var currentBol = missingBolRepository.BuildCurrentHandoffEntries(fleet.Drivers);
        var day = LocalDayRange.Create(
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        var result = new HandoffService().Generate(currentBol, fleet.Drivers, day);

        Assert.Equal(1, result.MissingBolDriverCount);
        Assert.Equal(1, result.MissingBolOrderCount);
        Assert.Contains("SYNH001", result.Text, StringComparison.Ordinal);
        Assert.Equal(before, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
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
        repository.ImportWorkbook(
            new MissingBolWorkbookImport("Synthetic Sheet", items),
            "Order Details Missing BOL-synthetic.xlsx",
            @"C:\Synthetic\Order Details Missing BOL-synthetic.xlsx",
            hash,
            new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero));

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
            "Order #", "TMEX Order #", "Logistics Order#", "Bill To", "Division#",
            "Empty Call Date", "Origin City St", "Destination City St", "Rev Type", "Terminal",
            "Driver Leader", "Driver Status", "Last Dispatch Driver cd", "Last Dispatch Driver nm",
            "Loaded Miles", "Order Level Order Miles"
        };
        var worksheetRows = new List<string> { BuildInlineRow(1, headers) };
        for (var index = 0; index < rows.Length; index++)
        {
            var source = rows[index];
            worksheetRows.Add(BuildInlineRow(
                index + 2,
                new[]
                {
                    source.Order, $"TMEX-{source.Order}", $"LOG-{source.Order}", "Synthetic Customer", "0611",
                    source.EmptyCallDate, "Boise, ID", "Auburn, WA", "Linehaul", "Synthetic Terminal",
                    "LEAD-BOL", "Active", source.DriverCode, source.DriverName, "125", "130"
                }));
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            WriteZipEntry(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            WriteZipEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                "<sheet name=\"Synthetic Sheet\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                string.Concat(worksheetRows) + "</sheetData></worksheet>");
        }

        return output.ToArray();
    }

    private static string BuildInlineRow(int rowNumber, IReadOnlyList<string> values)
    {
        var cells = values.Select((value, column) =>
            $"<c r=\"{ColumnName(column)}{rowNumber.ToString(CultureInfo.InvariantCulture)}\" t=\"inlineStr\"><is><t>{EscapeXml(value)}</t></is></c>");
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
}