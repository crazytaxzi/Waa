using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Waa.Core;

public sealed class MissingBolWorkbookParser
{
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] RequiredHeaders =
    [
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
    ];

    public MissingBolWorkbookImport Parse(byte[] workbookBytes)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        if (workbookBytes.Length == 0)
        {
            throw new ReportValidationException("Missing BOL workbook is empty.");
        }

        try
        {
            using var memory = new MemoryStream(workbookBytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            var entries = archive.Entries.ToDictionary(
                entry => NormalizePartName(entry.FullName),
                StringComparer.OrdinalIgnoreCase);

            var workbookEntry = GetRequiredEntry(entries, "xl/workbook.xml");
            var relationshipsEntry = GetRequiredEntry(entries, "xl/_rels/workbook.xml.rels");
            var workbookDocument = LoadXml(workbookEntry);
            var relationshipsDocument = LoadXml(relationshipsEntry);
            var sharedStrings = ReadSharedStrings(entries);
            var styles = ReadStyles(entries);
            var date1904 = IsDate1904(workbookDocument);
            var worksheets = ReadWorksheets(workbookDocument, relationshipsDocument);
            if (worksheets.Count == 0)
            {
                throw new ReportValidationException("Missing BOL workbook contains no worksheets.");
            }

            var worksheetFailures = new List<string>();
            foreach (var worksheet in worksheets)
            {
                if (!entries.TryGetValue(worksheet.PartName, out var worksheetEntry))
                {
                    worksheetFailures.Add($"{worksheet.Name}: worksheet part '{worksheet.PartName}' is missing");
                    continue;
                }

                var rows = ReadRows(worksheetEntry, sharedStrings, styles);
                var firstNonEmptyIndex = rows.FindIndex(row => row.Cells.Values.Any(cell =>
                    !string.IsNullOrWhiteSpace(cell.Text)));
                if (firstNonEmptyIndex < 0)
                {
                    worksheetFailures.Add($"{worksheet.Name}: worksheet is empty");
                    continue;
                }

                var headerAnalysis = HeaderMap.Analyze(rows[firstNonEmptyIndex]);
                if (headerAnalysis.AmbiguousRequiredHeaders.Count > 0)
                {
                    throw new ReportValidationException(
                        $"Worksheet '{worksheet.Name}' has ambiguous required header(s) in row " +
                        $"{rows[firstNonEmptyIndex].RowNumber.ToString(CultureInfo.InvariantCulture)}: " +
                        $"{string.Join(", ", headerAnalysis.AmbiguousRequiredHeaders)}.");
                }

                if (headerAnalysis.Map is null)
                {
                    worksheetFailures.Add(
                        $"{worksheet.Name}: missing {string.Join(", ", headerAnalysis.MissingRequiredHeaders)}");
                    continue;
                }

                var items = ParseItems(
                    worksheet.Name,
                    rows.Skip(firstNonEmptyIndex + 1),
                    headerAnalysis.Map,
                    date1904);
                return new MissingBolWorkbookImport(worksheet.Name, items);
            }

            var detail = worksheetFailures.Count == 0
                ? string.Join(", ", RequiredHeaders)
                : string.Join("; ", worksheetFailures);
            throw new ReportValidationException(
                $"Missing BOL workbook has no worksheet whose first non-empty row contains all required headers. {detail}.");
        }
        catch (ReportValidationException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ReportValidationException($"Missing BOL workbook is not a valid XLSX file: {exception.Message}");
        }
        catch (IOException exception)
        {
            throw new ReportValidationException($"Missing BOL workbook could not be read: {exception.Message}");
        }
        catch (System.Xml.XmlException exception)
        {
            throw new ReportValidationException($"Missing BOL workbook contains invalid XML: {exception.Message}");
        }
    }

    private static IReadOnlyList<MissingBolSourceItem> ParseItems(
        string worksheetName,
        IEnumerable<WorksheetRow> rows,
        HeaderMap headers,
        bool date1904)
    {
        var itemsByOrder = new Dictionary<string, MissingBolSourceItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!row.Cells.Values.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)))
            {
                continue;
            }

            var sourceOrderNumber = headers.GetText(row, "Order #").Trim();
            if (sourceOrderNumber.Length == 0)
            {
                throw ValidationError(
                    worksheetName,
                    row,
                    headers,
                    "Order #",
                    "is blank");
            }

            var normalizedOrderNumber = MissingBolText.NormalizeExact(sourceOrderNumber);
            var emptyCallDateCell = headers.GetCell(row, "Empty Call Date");
            var emptyCallDate = ParseRequiredDate(
                emptyCallDateCell,
                date1904,
                sourceOrderNumber,
                worksheetName,
                row.RowNumber,
                headers.GetColumnIndex("Empty Call Date"));
            var sourceDriverCode = headers.GetText(row, "Last Dispatch Driver cd").Trim();

            var item = new MissingBolSourceItem(
                normalizedOrderNumber,
                sourceOrderNumber,
                headers.GetText(row, "TMEX Order #").Trim(),
                headers.GetText(row, "Logistics Order#").Trim(),
                headers.GetText(row, "Bill To").Trim(),
                headers.GetText(row, "Division#").Trim(),
                emptyCallDate,
                headers.GetText(row, "Origin City St").Trim(),
                headers.GetText(row, "Destination City St").Trim(),
                headers.GetText(row, "Rev Type").Trim(),
                headers.GetText(row, "Terminal").Trim(),
                headers.GetText(row, "Driver Leader").Trim(),
                headers.GetText(row, "Driver Status").Trim(),
                sourceDriverCode,
                MissingBolText.NormalizeExact(sourceDriverCode),
                headers.GetText(row, "Last Dispatch Driver nm").Trim(),
                ParseOptionalDecimal(
                    headers.GetCell(row, "Loaded Miles"),
                    "Loaded Miles",
                    sourceOrderNumber,
                    worksheetName,
                    row.RowNumber,
                    headers.GetColumnIndex("Loaded Miles")),
                ParseOptionalDecimal(
                    headers.GetCell(row, "Order Level Order Miles"),
                    "Order Level Order Miles",
                    sourceOrderNumber,
                    worksheetName,
                    row.RowNumber,
                    headers.GetColumnIndex("Order Level Order Miles")),
                row.RowNumber);

            if (!itemsByOrder.TryGetValue(normalizedOrderNumber, out var existing))
            {
                itemsByOrder.Add(normalizedOrderNumber, item);
                continue;
            }

            if (Equivalent(existing, item))
            {
                continue;
            }

            throw new ReportValidationException(
                $"Worksheet '{worksheetName}' contains conflicting rows for Order # '{sourceOrderNumber}' " +
                $"(rows {existing.SourceRowNumber.ToString(CultureInfo.InvariantCulture)} and " +
                $"{item.SourceRowNumber.ToString(CultureInfo.InvariantCulture)}). The complete Missing BOL import was rejected.");
        }

        return itemsByOrder.Values
            .OrderBy(item => item.EmptyCallDate)
            .ThenBy(item => item.SourceOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool Equivalent(MissingBolSourceItem left, MissingBolSourceItem right) =>
        left.NormalizedOrderNumber == right.NormalizedOrderNumber &&
        left.SourceOrderNumber == right.SourceOrderNumber &&
        left.TmexOrderNumber == right.TmexOrderNumber &&
        left.LogisticsOrderNumber == right.LogisticsOrderNumber &&
        left.BillTo == right.BillTo &&
        left.DivisionCode == right.DivisionCode &&
        left.EmptyCallDate == right.EmptyCallDate &&
        left.OriginCityState == right.OriginCityState &&
        left.DestinationCityState == right.DestinationCityState &&
        left.RevenueType == right.RevenueType &&
        left.Terminal == right.Terminal &&
        left.SourceDriverLeader == right.SourceDriverLeader &&
        left.SourceDriverStatus == right.SourceDriverStatus &&
        left.SourceDriverCode == right.SourceDriverCode &&
        left.NormalizedSourceDriverCode == right.NormalizedSourceDriverCode &&
        left.SourceDriverName == right.SourceDriverName &&
        left.LoadedMiles == right.LoadedMiles &&
        left.OrderLevelMiles == right.OrderLevelMiles;

    private static DateOnly ParseRequiredDate(
        WorksheetCell cell,
        bool date1904,
        string orderNumber,
        string worksheetName,
        int rowNumber,
        int columnIndex)
    {
        if (cell.IsNumeric && double.TryParse(
                cell.RawValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var serial))
        {
            try
            {
                if (date1904)
                {
                    serial += 1462d;
                }

                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException)
            {
                // The detailed validation error below includes the order and source cell.
            }
        }

        var value = cell.Text.Trim();
        if (DateOnly.TryParseExact(
                value,
                ["M/d/yy", "M/d/yyyy", "MM/dd/yy", "MM/dd/yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        throw new ReportValidationException(
            $"Order # '{orderNumber}' has invalid Empty Call Date '{value}' at worksheet " +
            $"'{worksheetName}' cell {ColumnName(columnIndex)}{rowNumber.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static decimal? ParseOptionalDecimal(
        WorksheetCell cell,
        string header,
        string orderNumber,
        string worksheetName,
        int rowNumber,
        int columnIndex)
    {
        var value = cell.Text.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        throw new ReportValidationException(
            $"Order # '{orderNumber}' has invalid {header} value '{value}' at worksheet " +
            $"'{worksheetName}' cell {ColumnName(columnIndex)}{rowNumber.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static ReportValidationException ValidationError(
        string worksheetName,
        WorksheetRow row,
        HeaderMap headers,
        string header,
        string detail) =>
        new(
            $"Worksheet '{worksheetName}' cell {ColumnName(headers.GetColumnIndex(header))}" +
            $"{row.RowNumber.ToString(CultureInfo.InvariantCulture)} ({header}) {detail}.");

    private static List<WorksheetRow> ReadRows(
        ZipArchiveEntry entry,
        IReadOnlyList<string> sharedStrings,
        StyleTable styles)
    {
        var document = LoadXml(entry);
        var rows = new List<WorksheetRow>();
        var fallbackRowNumber = 0;

        foreach (var rowElement in document.Descendants(SpreadsheetNamespace + "row"))
        {
            fallbackRowNumber++;
            var rowNumber = ParsePositiveInt((string?)rowElement.Attribute("r")) ?? fallbackRowNumber;
            var cells = new Dictionary<int, WorksheetCell>();
            var fallbackColumnIndex = 0;

            foreach (var cellElement in rowElement.Elements(SpreadsheetNamespace + "c"))
            {
                var reference = (string?)cellElement.Attribute("r");
                var columnIndex = string.IsNullOrWhiteSpace(reference)
                    ? fallbackColumnIndex
                    : ParseColumnIndex(reference);
                fallbackColumnIndex = columnIndex + 1;
                cells[columnIndex] = ReadCell(cellElement, sharedStrings, styles);
            }

            rows.Add(new WorksheetRow(rowNumber, cells));
        }

        return rows;
    }

    private static WorksheetCell ReadCell(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        StyleTable styles)
    {
        var type = (string?)cell.Attribute("t") ?? string.Empty;
        var styleIndex = ParsePositiveIntAllowZero((string?)cell.Attribute("s")) ?? 0;
        var rawValue = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;

        if (type.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            var inline = string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
            return new WorksheetCell(inline, inline, false);
        }

        if (type.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                index < 0 || index >= sharedStrings.Count)
            {
                throw new ReportValidationException($"XLSX shared-string index '{rawValue}' is invalid.");
            }

            return new WorksheetCell(sharedStrings[index], rawValue, false);
        }

        if (type.Equals("b", StringComparison.OrdinalIgnoreCase))
        {
            return new WorksheetCell(rawValue == "1" ? "TRUE" : "FALSE", rawValue, false);
        }

        if (type.Equals("str", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("d", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("e", StringComparison.OrdinalIgnoreCase))
        {
            return new WorksheetCell(rawValue, rawValue, false);
        }

        if (rawValue.Length == 0)
        {
            return WorksheetCell.Empty;
        }

        return new WorksheetCell(
            FormatNumericText(rawValue, styles.GetFormatCode(styleIndex)),
            rawValue,
            true);
    }

    private static string FormatNumericText(string rawValue, string? formatCode)
    {
        if (!decimal.TryParse(
                rawValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var numeric))
        {
            return rawValue;
        }

        var padding = GetZeroPadding(formatCode);
        if (padding > 0 && numeric == decimal.Truncate(numeric))
        {
            return decimal.Truncate(numeric)
                .ToString(new string('0', padding), CultureInfo.InvariantCulture);
        }

        return numeric.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static int GetZeroPadding(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return 0;
        }

        var firstSection = formatCode.Split(';')[0].Trim();
        var zeroCount = 0;
        for (var index = 0; index < firstSection.Length; index++)
        {
            var character = firstSection[index];
            if (character == '\\' && index + 1 < firstSection.Length)
            {
                index++;
                continue;
            }

            if (character == '0')
            {
                zeroCount++;
                continue;
            }

            if (character is ' ' or '"')
            {
                continue;
            }

            return 0;
        }

        return zeroCount;
    }

    private static IReadOnlyList<string> ReadSharedStrings(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!entries.TryGetValue("xl/sharedStrings.xml", out var entry))
        {
            return Array.Empty<string>();
        }

        var document = LoadXml(entry);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static StyleTable ReadStyles(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!entries.TryGetValue("xl/styles.xml", out var entry))
        {
            return StyleTable.Empty;
        }

        var document = LoadXml(entry);
        var customFormats = document
            .Descendants(SpreadsheetNamespace + "numFmt")
            .Select(format => new
            {
                Id = ParsePositiveIntAllowZero((string?)format.Attribute("numFmtId")),
                Code = (string?)format.Attribute("formatCode")
            })
            .Where(format => format.Id is not null && format.Code is not null)
            .ToDictionary(format => format.Id!.Value, format => format.Code!, EqualityComparer<int>.Default);

        var formats = document
            .Descendants(SpreadsheetNamespace + "cellXfs")
            .Elements(SpreadsheetNamespace + "xf")
            .Select(format =>
            {
                var id = ParsePositiveIntAllowZero((string?)format.Attribute("numFmtId")) ?? 0;
                return customFormats.TryGetValue(id, out var code) ? code : null;
            })
            .ToArray();
        return new StyleTable(formats);
    }

    private static bool IsDate1904(XDocument workbookDocument) =>
        workbookDocument
            .Descendants(SpreadsheetNamespace + "workbookPr")
            .Select(element => (string?)element.Attribute("date1904"))
            .Any(value => value is "1" or "true" or "TRUE");

    private static IReadOnlyList<WorksheetPart> ReadWorksheets(
        XDocument workbookDocument,
        XDocument relationshipsDocument)
    {
        var relationships = relationshipsDocument
            .Descendants(PackageRelationshipNamespace + "Relationship")
            .Where(relationship =>
                ((string?)relationship.Attribute("Type"))?.EndsWith(
                    "/worksheet",
                    StringComparison.OrdinalIgnoreCase) == true)
            .ToDictionary(
                relationship => (string?)relationship.Attribute("Id") ?? string.Empty,
                relationship => ResolvePartName(
                    "xl/workbook.xml",
                    (string?)relationship.Attribute("Target") ?? string.Empty),
                StringComparer.Ordinal);

        var worksheets = new List<WorksheetPart>();
        foreach (var sheet in workbookDocument.Descendants(SpreadsheetNamespace + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? "Unnamed worksheet";
            var relationshipId = (string?)sheet.Attribute(RelationshipNamespace + "id") ?? string.Empty;
            if (relationships.TryGetValue(relationshipId, out var partName))
            {
                worksheets.Add(new WorksheetPart(name, partName));
            }
        }

        return worksheets;
    }

    private static string ResolvePartName(string sourcePartName, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        if (target.StartsWith('/', StringComparison.Ordinal))
        {
            return NormalizePartName(target.TrimStart('/'));
        }

        var sourceDirectory = sourcePartName.Contains('/', StringComparison.Ordinal)
            ? sourcePartName[..sourcePartName.LastIndexOf('/', StringComparison.Ordinal)]
            : string.Empty;
        var segments = new List<string>();
        foreach (var segment in $"{sourceDirectory}/{target}".Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return NormalizePartName(string.Join('/', segments));
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string partName) =>
        entries.TryGetValue(partName, out var entry)
            ? entry
            : throw new ReportValidationException($"Missing BOL workbook is missing required XLSX part '{partName}'.");

    private static int ParseColumnIndex(string reference)
    {
        var index = 0;
        var foundLetter = false;
        foreach (var character in reference)
        {
            if (!char.IsAsciiLetter(character))
            {
                break;
            }

            foundLetter = true;
            index = checked(index * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
        }

        if (!foundLetter)
        {
            throw new ReportValidationException($"XLSX cell reference '{reference}' is invalid.");
        }

        return index - 1;
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

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static int? ParsePositiveIntAllowZero(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static string NormalizePartName(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string NormalizeHeader(string value)
    {
        var source = value.TrimStart('\uFEFF').Replace('\u00A0', ' ').Trim();
        var output = new StringBuilder(source.Length);
        var previousWasWhitespace = false;
        foreach (var character in source)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    output.Append(' ');
                }

                previousWasWhitespace = true;
            }
            else
            {
                output.Append(character);
                previousWasWhitespace = false;
            }
        }

        return output.ToString();
    }

    private sealed record WorksheetPart(string Name, string PartName);

    private sealed record WorksheetRow(
        int RowNumber,
        IReadOnlyDictionary<int, WorksheetCell> Cells);

    private sealed record WorksheetCell(string Text, string RawValue, bool IsNumeric)
    {
        public static WorksheetCell Empty { get; } = new(string.Empty, string.Empty, false);
    }

    private sealed class StyleTable
    {
        private readonly IReadOnlyList<string?> _formatCodes;

        public StyleTable(IReadOnlyList<string?> formatCodes)
        {
            _formatCodes = formatCodes;
        }

        public static StyleTable Empty { get; } = new(Array.Empty<string?>());

        public string? GetFormatCode(int styleIndex) =>
            styleIndex >= 0 && styleIndex < _formatCodes.Count
                ? _formatCodes[styleIndex]
                : null;
    }

    private sealed record HeaderAnalysis(
        HeaderMap? Map,
        IReadOnlyList<string> MissingRequiredHeaders,
        IReadOnlyList<string> AmbiguousRequiredHeaders);

    private sealed class HeaderMap
    {
        private readonly IReadOnlyDictionary<string, int> _columns;

        private HeaderMap(IReadOnlyDictionary<string, int> columns)
        {
            _columns = columns;
        }

        public static HeaderAnalysis Analyze(WorksheetRow headerRow)
        {
            var locations = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (columnIndex, cell) in headerRow.Cells)
            {
                var normalized = NormalizeHeader(cell.Text);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!locations.TryGetValue(normalized, out var columns))
                {
                    columns = new List<int>();
                    locations.Add(normalized, columns);
                }

                columns.Add(columnIndex);
            }

            var missing = RequiredHeaders
                .Where(header => !locations.ContainsKey(header))
                .ToArray();
            var ambiguous = RequiredHeaders
                .Where(header => locations.TryGetValue(header, out var columns) && columns.Count > 1)
                .ToArray();
            if (missing.Length > 0 || ambiguous.Length > 0)
            {
                return new HeaderAnalysis(null, missing, ambiguous);
            }

            return new HeaderAnalysis(
                new HeaderMap(RequiredHeaders.ToDictionary(
                    header => header,
                    header => locations[header][0],
                    StringComparer.OrdinalIgnoreCase)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        public int GetColumnIndex(string header) => _columns[header];

        public WorksheetCell GetCell(WorksheetRow row, string header) =>
            row.Cells.TryGetValue(GetColumnIndex(header), out var cell)
                ? cell
                : WorksheetCell.Empty;

        public string GetText(WorksheetRow row, string header) => GetCell(row, header).Text;
    }
}
