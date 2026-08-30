using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Waa.Core;
using Xunit;

namespace Waa.Core.Tests;

public sealed class MissingBolWorkbookParserTests
{
    [Fact]
    public void Parse_FindsQualifyingWorksheetAndToleratesDuplicateIrrelevantHeaders()
    {
        var headers = FullHeaders();
        headers[0] = "\uFEFF  Order   #  ";
        headers[20] = " Driver\u00A0 Leader ";
        var workbook = SyntheticXlsx.Build(
            new SyntheticSheet("Notes", [Row(Inline("This is not the export header."))]),
            new SyntheticSheet(
                "Operational Export",
                [HeaderRow(headers), DataRow(order: Shared("SYN1001"))]));

        var result = new MissingBolWorkbookParser().Parse(workbook);

        Assert.Equal("Operational Export", result.WorksheetName);
        var item = Assert.Single(result.Items);
        Assert.Equal("SYN1001", item.SourceOrderNumber);
        Assert.Equal("LEAD-BOL", item.SourceDriverLeader);
        Assert.Equal("Synthetic Terminal", item.Terminal);
    }

    [Fact]
    public void Parse_DistinguishesDriverLeaderFromDuplicateTerminalLeaderColumns()
    {
        var row = DataRow(order: Inline("SYN1002"));
        row[16] = Inline("TERMINAL-A");
        row[20] = Inline("DRIVER-LEADER");
        row[24] = Inline("TERMINAL-B");
        row[25] = Inline("TERMINAL-C");

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal("DRIVER-LEADER", item.SourceDriverLeader);
        Assert.DoesNotContain("TERMINAL", item.SourceDriverLeader, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsSharedStringAndInlineStringCells()
    {
        var row = DataRow(order: Shared("SYN1003"));
        row[3] = Inline("Synthetic Customer");
        row[22] = Shared("A00001");

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal("SYN1003", item.SourceOrderNumber);
        Assert.Equal("Synthetic Customer", item.BillTo);
        Assert.Equal("A00001", item.SourceDriverCode);
    }

    [Fact]
    public void Parse_FormatsNumericIdentifiersWithoutScientificNotation()
    {
        var row = DataRow(order: Numeric("1.001E+5"));
        row[1] = Numeric("9.876E+8");
        row[22] = Numeric("123456");

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal("100100", item.SourceOrderNumber);
        Assert.Equal("987600000", item.TmexOrderNumber);
        Assert.Equal("123456", item.SourceDriverCode);
        Assert.DoesNotContain("E", item.SourceOrderNumber, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_PreservesLeadingZerosWhenWorkbookNumberFormatSuppliesThem()
    {
        var row = DataRow(order: Numeric("42", styleIndex: 1));
        row[22] = Numeric("123", styleIndex: 1);

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal("000042", item.SourceOrderNumber);
        Assert.Equal("000123", item.SourceDriverCode);
        Assert.Equal("000123", item.NormalizedSourceDriverCode);
    }

    [Fact]
    public void Parse_ReadsTextAndExcelSerialEmptyCallDates()
    {
        var textRow = DataRow(order: Inline("SYN1004"), emptyCallDate: Inline("8/27/26"));
        var serial = new DateTime(2026, 8, 28).ToOADate().ToString(CultureInfo.InvariantCulture);
        var serialRow = DataRow(order: Inline("SYN1005"), emptyCallDate: Numeric(serial, styleIndex: 2));

        var result = Parse(FullHeaders(), textRow, serialRow);

        Assert.Equal(new DateOnly(2026, 8, 27), result.Items.Single(item => item.SourceOrderNumber == "SYN1004").EmptyCallDate);
        Assert.Equal(new DateOnly(2026, 8, 28), result.Items.Single(item => item.SourceOrderNumber == "SYN1005").EmptyCallDate);
    }

    [Fact]
    public void Parse_ReadsMultipleOrdersForOneDriver()
    {
        var first = DataRow(order: Inline("SYN1006"));
        var second = DataRow(order: Inline("SYN1007"));
        first[22] = Inline("A00001");
        second[22] = Inline("A00001");

        var result = Parse(FullHeaders(), first, second);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("A00001", item.NormalizedSourceDriverCode));
    }

    [Fact]
    public void Parse_RejectsBlankOrderNumber()
    {
        var exception = Assert.Throws<ReportValidationException>(() =>
            Parse(FullHeaders(), DataRow(order: Inline("   "))));

        Assert.Contains("Order #", exception.Message, StringComparison.Ordinal);
        Assert.Contains("blank", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsInvalidEmptyCallDateWithOrderAndCell()
    {
        var exception = Assert.Throws<ReportValidationException>(() =>
            Parse(
                FullHeaders(),
                DataRow(order: Inline("SYN1008"), emptyCallDate: Inline("not-a-date"))));

        Assert.Contains("SYN1008", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Empty Call Date", exception.Message, StringComparison.Ordinal);
        Assert.Contains("G2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CollapsesTrulyIdenticalDuplicateOrderRows()
    {
        var row = DataRow(order: Inline("SYN1009"));

        var result = Parse(FullHeaders(), row, row.ToArray());

        Assert.Single(result.Items);
    }

    [Fact]
    public void Parse_RejectsConflictingDuplicateOrderRows()
    {
        var first = DataRow(order: Inline("SYN1010"));
        var second = DataRow(order: Inline("syn1010"));
        second[8] = Inline("Different, ZZ");

        var exception = Assert.Throws<ReportValidationException>(() =>
            Parse(FullHeaders(), first, second));

        Assert.Contains("conflicting rows", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SYN1010", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IgnoresExtraColumnsAndDoesNotRequireTotalRevenue()
    {
        var headers = FullHeaders().Take(28).Append("Irrelevant Extra Field").ToArray();
        var row = DataRow(order: Inline("SYN1011")).Take(28).Append(Inline("Ignored")).ToArray();

        var item = Assert.Single(Parse(headers, row).Items);

        Assert.Equal("SYN1011", item.SourceOrderNumber);
        Assert.Equal(125.5m, item.LoadedMiles);
    }

    [Fact]
    public void Parse_RejectsAmbiguousRequiredHeader()
    {
        var headers = FullHeaders().Append("Order #").ToArray();
        var row = DataRow(order: Inline("SYN1012")).Append(Inline("SYN1012")).ToArray();

        var exception = Assert.Throws<ReportValidationException>(() => Parse(headers, row));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Order #", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PreservesBlankDriverCodeAsUnmatchedEvidence()
    {
        var row = DataRow(order: Inline("SYN1013"));
        row[22] = Blank();
        row[23] = Inline("Unknown Synthetic Driver");

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal(string.Empty, item.SourceDriverCode);
        Assert.Equal(string.Empty, item.NormalizedSourceDriverCode);
        Assert.Equal("Unknown Synthetic Driver", item.SourceDriverName);
    }

    [Fact]
    public void Parse_NormalizesExactDriverCodeWithoutInventingPunctuation()
    {
        var row = DataRow(order: Inline("SYN1014"));
        row[22] = Inline("  ab-12  ");

        var item = Assert.Single(Parse(FullHeaders(), row).Items);

        Assert.Equal("ab-12", item.SourceDriverCode);
        Assert.Equal("AB-12", item.NormalizedSourceDriverCode);
    }

    private static MissingBolWorkbookImport Parse(
        IReadOnlyList<string> headers,
        params XlsxCell[][] rows) =>
        new MissingBolWorkbookParser().Parse(SyntheticXlsx.Build(
            new SyntheticSheet("Sheet 1", [HeaderRow(headers), .. rows])));

    private static XlsxCell[] HeaderRow(IReadOnlyList<string> headers) =>
        headers.Select((header, index) => index % 2 == 0 ? Shared(header) : Inline(header)).ToArray();

    private static XlsxCell[] DataRow(
        XlsxCell order,
        XlsxCell? emptyCallDate = null)
    {
        var row = Enumerable.Range(0, 29).Select(_ => Blank()).ToArray();
        row[0] = order;
        row[1] = Inline("TMEX-SYN");
        row[2] = Inline("LOG-SYN");
        row[3] = Inline("Synthetic Bill To");
        row[4] = Inline("0611");
        row[6] = emptyCallDate ?? Inline("8/27/2026");
        row[7] = Inline("Boise, ID");
        row[8] = Inline("Auburn, WA");
        row[14] = Inline("Linehaul");
        row[15] = Inline("Synthetic Terminal");
        row[16] = Inline("TERMINAL-A");
        row[20] = Inline("LEAD-BOL");
        row[21] = Inline("Active");
        row[22] = Inline("A00001");
        row[23] = Inline("Alex Source Name");
        row[24] = Inline("TERMINAL-B");
        row[25] = Inline("TERMINAL-C");
        row[26] = Numeric("125.5");
        row[27] = Numeric("130");
        row[28] = Numeric("999.99");
        return row;
    }

    private static string[] FullHeaders() =>
    [
        "Order #",
        "TMEX Order #",
        "Logistics Order#",
        "Bill To",
        "Division#",
        "Shipper LOB",
        "Empty Call Date",
        "Origin City St",
        "Destination City St",
        "Billing Leader",
        "Billing Analyst",
        "AR Leader",
        "AR Analyst",
        "Bankq flg ",
        "Rev Type",
        "Terminal ",
        "Terminal Leader ",
        "Buyer",
        "Carrier",
        " Dray Name",
        "Driver Leader ",
        "Driver Status",
        "Last Dispatch Driver cd",
        "Last Dispatch Driver nm",
        "Terminal Leader ",
        "Terminal Leader ",
        "Loaded Miles",
        "Order Level Order Miles",
        "Total Revenue"
    ];

    private static XlsxCell[] Row(params XlsxCell[] cells) => cells;
    private static XlsxCell Blank() => new(string.Empty, XlsxCellKind.Blank, 0);
    private static XlsxCell Inline(string value) => new(value, XlsxCellKind.InlineString, 0);
    private static XlsxCell Shared(string value) => new(value, XlsxCellKind.SharedString, 0);
    private static XlsxCell Numeric(string value, int styleIndex = 0) =>
        new(value, XlsxCellKind.Numeric, styleIndex);
}

internal enum XlsxCellKind
{
    Blank,
    InlineString,
    SharedString,
    Numeric
}

internal sealed record XlsxCell(string Value, XlsxCellKind Kind, int StyleIndex);

internal sealed record SyntheticSheet(string Name, IReadOnlyList<XlsxCell[]> Rows);

internal static class SyntheticXlsx
{
    public static byte[] Build(params SyntheticSheet[] sheets)
    {
        if (sheets.Length == 0)
        {
            throw new ArgumentException("At least one synthetic worksheet is required.", nameof(sheets));
        }

        var sharedValues = sheets
            .SelectMany(sheet => sheet.Rows)
            .SelectMany(row => row)
            .Where(cell => cell.Kind == XlsxCellKind.SharedString)
            .Select(cell => cell.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sharedIndices = sharedValues
            .Select((value, index) => (value, index))
            .ToDictionary(pair => pair.value, pair => pair.index, StringComparer.Ordinal);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes(sheets.Length));
            Write(archive, "_rels/.rels", RootRelationships);
            Write(archive, "xl/workbook.xml", Workbook(sheets));
            Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Length));
            Write(archive, "xl/styles.xml", Styles);
            Write(archive, "xl/sharedStrings.xml", SharedStrings(sharedValues));

            for (var index = 0; index < sheets.Length; index++)
            {
                Write(
                    archive,
                    $"xl/worksheets/sheet{index + 1}.xml",
                    Worksheet(sheets[index], sharedIndices));
            }
        }

        return output.ToArray();
    }

    private static string ContentTypes(int sheetCount)
    {
        var worksheetOverrides = string.Join(
            string.Empty,
            Enumerable.Range(1, sheetCount).Select(index =>
                $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
               "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
               "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
               "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
               "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
               "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
               worksheetOverrides +
               "</Types>";
    }

    private static string Workbook(IReadOnlyList<SyntheticSheet> sheets)
    {
        var sheetXml = string.Join(
            string.Empty,
            sheets.Select((sheet, index) =>
                $"<sheet name=\"{Escape(sheet.Name)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
               "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               $"<sheets>{sheetXml}</sheets></workbook>";
    }

    private static string WorkbookRelationships(int sheetCount)
    {
        var relationships = string.Join(
            string.Empty,
            Enumerable.Range(1, sheetCount).Select(index =>
                $"<Relationship Id=\"rId{index}\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
                $"Target=\"worksheets/sheet{index}.xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               relationships +
               $"<Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
               $"<Relationship Id=\"rId{sheetCount + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
               "</Relationships>";
    }

    private static string SharedStrings(IReadOnlyList<string> values)
    {
        var items = string.Join(
            string.Empty,
            values.Select(value => $"<si><t xml:space=\"preserve\">{Escape(value)}</t></si>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
               $"count=\"{values.Count}\" uniqueCount=\"{values.Count}\">{items}</sst>";
    }

    private static string Worksheet(
        SyntheticSheet sheet,
        IReadOnlyDictionary<string, int> sharedIndices)
    {
        var rows = new StringBuilder();
        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            rows.Append("<row r=\"");
            rows.Append(rowIndex + 1);
            rows.Append("\">");
            var row = sheet.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                var cell = row[columnIndex];
                if (cell.Kind == XlsxCellKind.Blank)
                {
                    continue;
                }

                var reference = $"{ColumnName(columnIndex)}{rowIndex + 1}";
                switch (cell.Kind)
                {
                    case XlsxCellKind.InlineString:
                        rows.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(cell.Value)}</t></is></c>");
                        break;
                    case XlsxCellKind.SharedString:
                        rows.Append($"<c r=\"{reference}\" t=\"s\"><v>{sharedIndices[cell.Value]}</v></c>");
                        break;
                    case XlsxCellKind.Numeric:
                        var style = cell.StyleIndex > 0 ? $" s=\"{cell.StyleIndex}\"" : string.Empty;
                        rows.Append($"<c r=\"{reference}\"{style}><v>{Escape(cell.Value)}</v></c>");
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported synthetic cell kind.");
                }
            }

            rows.Append("</row>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
               $"<sheetData>{rows}</sheetData></worksheet>";
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBasedIndex)
    {
        var value = zeroBasedIndex + 1;
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private const string RootRelationships =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"000000\"/></numFmts>" +
        "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs></styleSheet>";
}
