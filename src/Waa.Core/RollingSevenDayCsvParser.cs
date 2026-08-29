using System.Globalization;
using System.Text;

namespace Waa.Core;

public sealed class RollingSevenDayCsvParser
{
    private const string DriverLabelHeader = "Group by (copy)";
    private const string MeasureNameHeader = "Measure Names";
    private const string WeekDateHeader = "Week Start Date";
    private const string EngineHoursHeader = "[Rolling 7 Day Engine Time]/60";
    private const string IdleHoursHeader = "[Rolling 7 Day Idle Time]/60";
    private const string CostCenterHeader = "Cost Center";
    private const string DriverLeaderHeader = "Driver Leader";
    private const string DriverTerminalHeader = "Driver Terminal";
    private const string FleetLeaderHeader = "Fleet Leader";
    private const string OpsLobHeader = "OPS LOB";
    private const string UnitCodeHeader = "Unit Code";

    public RollingSevenDayImport Parse(byte[] csvBytes)
    {
        ArgumentNullException.ThrowIfNull(csvBytes);

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(csvBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ReportValidationException(
                $"Rolling 7 Day CSV is not valid UTF-8: {exception.Message}");
        }

        return Parse(text);
    }

    public RollingSevenDayImport Parse(string csvText)
    {
        var rows = CsvReader.Read(csvText);
        if (rows.Count < 2)
        {
            throw new ReportValidationException("Rolling 7 Day CSV has no data rows.");
        }

        var headers = HeaderMap.Create(rows[0]);
        var sourceRows = new List<SourceRow>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var driver = DriverLabelParser.Parse(headers.Get(row, DriverLabelHeader, rowIndex));
            var measureName = headers.Get(row, MeasureNameHeader, rowIndex).Trim();
            if (!measureName.Equals("Idle %", StringComparison.OrdinalIgnoreCase) &&
                !measureName.Equals("OOR %", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var weekDate = ParseDate(headers.Get(row, WeekDateHeader, rowIndex), WeekDateHeader, rowIndex);
            var engineHours = ParseNonNegativeDecimal(
                headers.Get(row, EngineHoursHeader, rowIndex),
                EngineHoursHeader,
                rowIndex);
            var idleHours = ParseNonNegativeDecimal(
                headers.Get(row, IdleHoursHeader, rowIndex),
                IdleHoursHeader,
                rowIndex);

            sourceRows.Add(new SourceRow(
                driver,
                measureName,
                weekDate,
                engineHours,
                idleHours,
                headers.Get(row, UnitCodeHeader, rowIndex).Trim(),
                DriverLabelParser.ParseDriverLeader(headers.Get(row, DriverLeaderHeader, rowIndex)),
                headers.Get(row, DriverTerminalHeader, rowIndex).Trim(),
                headers.Get(row, FleetLeaderHeader, rowIndex).Trim(),
                headers.Get(row, CostCenterHeader, rowIndex).Trim(),
                headers.Get(row, OpsLobHeader, rowIndex).Trim(),
                rowIndex + 1));
        }

        if (sourceRows.Count == 0)
        {
            throw new ReportValidationException("Rolling 7 Day CSV has no Idle % or OOR % rows.");
        }

        var observations = Normalize(sourceRows);
        var reportCycleDate = observations.Max(observation => observation.WeekDate);
        var currentDriverCodes = observations
            .Where(observation => observation.WeekDate == reportCycleDate)
            .Select(observation => observation.Driver.DriverCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var driverSnapshots = currentDriverCodes
            .Select(driverCode => IdleCalculator.CalculateDriver(driverCode, reportCycleDate, observations))
            .OrderBy(snapshot => snapshot.Driver.DriverName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RollingSevenDayImport(
            reportCycleDate,
            observations,
            driverSnapshots,
            IdleCalculator.CalculateFleet(reportCycleDate, driverSnapshots));
    }

    private static IReadOnlyList<WeeklyDriverObservation> Normalize(IReadOnlyCollection<SourceRow> sourceRows)
    {
        var observations = new List<WeeklyDriverObservation>();

        foreach (var group in sourceRows.GroupBy(row => new ObservationKey(row.Driver.DriverCode, row.WeekDate)))
        {
            var rows = group.ToArray();
            var measureNames = rows
                .Select(row => row.MeasureName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!measureNames.Contains("Idle %") || !measureNames.Contains("OOR %"))
            {
                throw new ReportValidationException(
                    $"Driver '{group.Key.DriverCode}' week {group.Key.WeekDate:yyyy-MM-dd} is missing its Idle % or OOR % companion row.");
            }

            var first = rows[0];
            foreach (var candidate in rows.Skip(1))
            {
                EnsureEquivalent(first, candidate);
            }

            observations.Add(new WeeklyDriverObservation(
                first.Driver,
                first.WeekDate,
                first.EngineHours,
                first.IdleHours,
                first.UnitCode,
                first.DriverLeader,
                first.DriverTerminal,
                first.FleetLeader,
                first.CostCenter,
                first.OpsLob));
        }

        return observations
            .OrderBy(observation => observation.Driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.WeekDate)
            .ToArray();
    }

    private static void EnsureEquivalent(SourceRow expected, SourceRow candidate)
    {
        var differences = new List<string>();
        AddDifference(differences, "Driver Name", expected.Driver.DriverName, candidate.Driver.DriverName);
        AddDifference(differences, "Engine Hours", expected.EngineHours, candidate.EngineHours);
        AddDifference(differences, "Idle Hours", expected.IdleHours, candidate.IdleHours);
        AddDifference(differences, "Unit Code", expected.UnitCode, candidate.UnitCode);
        AddDifference(differences, "Driver Leader", expected.DriverLeader, candidate.DriverLeader);
        AddDifference(differences, "Driver Terminal", expected.DriverTerminal, candidate.DriverTerminal);
        AddDifference(differences, "Fleet Leader", expected.FleetLeader, candidate.FleetLeader);
        AddDifference(differences, "Cost Center", expected.CostCenter, candidate.CostCenter);
        AddDifference(differences, "OPS LOB", expected.OpsLob, candidate.OpsLob);

        if (differences.Count > 0)
        {
            throw new ReportValidationException(
                $"Conflicting repeated rows for driver '{expected.Driver.DriverCode}' week {expected.WeekDate:yyyy-MM-dd} " +
                $"(CSV rows {expected.CsvLineNumber} and {candidate.CsvLineNumber}): {string.Join(", ", differences)}.");
        }
    }

    private static void AddDifference<T>(List<string> differences, string field, T left, T right)
    {
        if (!EqualityComparer<T>.Default.Equals(left, right))
        {
            differences.Add(field);
        }
    }

    private static DateOnly ParseDate(string value, string field, int rowIndex)
    {
        if (DateOnly.TryParseExact(
            value.Trim(),
            ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        throw new ReportValidationException(
            $"CSV row {rowIndex + 1} has invalid {field} value '{value}'.");
    }

    private static decimal ParseNonNegativeDecimal(string value, string field, int rowIndex)
    {
        if (!decimal.TryParse(
            value.Trim(),
            NumberStyles.Number | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            throw new ReportValidationException(
                $"CSV row {rowIndex + 1} has invalid {field} value '{value}'.");
        }

        if (parsed < 0)
        {
            throw new ReportValidationException(
                $"CSV row {rowIndex + 1} has negative {field} value '{value}'.");
        }

        return parsed;
    }

    private readonly record struct ObservationKey(string DriverCode, DateOnly WeekDate);

    private sealed record SourceRow(
        DriverIdentity Driver,
        string MeasureName,
        DateOnly WeekDate,
        decimal EngineHours,
        decimal IdleHours,
        string UnitCode,
        string DriverLeader,
        string DriverTerminal,
        string FleetLeader,
        string CostCenter,
        string OpsLob,
        int CsvLineNumber);

    private sealed class HeaderMap
    {
        private readonly IReadOnlyDictionary<string, int> _indices;

        private HeaderMap(IReadOnlyDictionary<string, int> indices)
        {
            _indices = indices;
        }

        public static HeaderMap Create(IReadOnlyList<string> headers)
        {
            var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count; index++)
            {
                var normalized = NormalizeHeader(headers[index]);
                if (!indices.ContainsKey(normalized))
                {
                    indices.Add(normalized, index);
                }
            }

            var required = new[]
            {
                DriverLabelHeader,
                MeasureNameHeader,
                WeekDateHeader,
                EngineHoursHeader,
                IdleHoursHeader,
                CostCenterHeader,
                DriverLeaderHeader,
                DriverTerminalHeader,
                FleetLeaderHeader,
                OpsLobHeader,
                UnitCodeHeader
            };

            var missing = required.Where(header => !indices.ContainsKey(header)).ToArray();
            if (missing.Length > 0)
            {
                throw new ReportValidationException(
                    $"Rolling 7 Day CSV is missing required header(s): {string.Join(", ", missing)}.");
            }

            return new HeaderMap(indices);
        }

        public string Get(IReadOnlyList<string> row, string header, int rowIndex)
        {
            var index = _indices[header];
            if (index >= row.Count)
            {
                throw new ReportValidationException(
                    $"CSV row {rowIndex + 1} ends before required column '{header}'.");
            }

            return row[index];
        }

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
    }
}
