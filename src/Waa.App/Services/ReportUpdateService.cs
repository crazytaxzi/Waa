using System.Globalization;
using System.Security.Cryptography;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.Core;

namespace Waa.App.Services;

public enum ReportSourceUpdateState
{
    Imported,
    Current,
    NotFound,
    SkippedOlder,
    Failed,
    NotConfigured
}

public sealed record ReportSourceUpdateResult(
    ReportSourceUpdateState State,
    bool Changed,
    string Message,
    string? SourceFile,
    DateOnly? ReportCycleDate)
{
    public bool Succeeded => State != ReportSourceUpdateState.Failed;
}

public sealed record ReportUpdateResult(
    bool Changed,
    string Message,
    string? SourceFile,
    DateOnly? ReportCycleDate,
    ReportSourceUpdateResult RollingSevenDay,
    ReportSourceUpdateResult MissingBol);

public sealed class ReportUpdateService
{
    private const long MaximumReportBytes = 100L * 1024L * 1024L;
    private readonly WaaRepository _repository;
    private readonly MissingBolRepository? _missingBolRepository;
    private readonly RollingSevenDayCsvParser _rollingParser;
    private readonly MissingBolWorkbookParser? _missingBolParser;
    private readonly Func<string> _downloadsLocator;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ReportUpdateService(WaaRepository repository, RollingSevenDayCsvParser parser)
    {
        _repository = repository;
        _rollingParser = parser;
        _missingBolRepository = null;
        _missingBolParser = null;
        _downloadsLocator = DownloadsLocator.GetDownloadsFolder;
    }

    public ReportUpdateService(
        WaaRepository repository,
        MissingBolRepository missingBolRepository,
        RollingSevenDayCsvParser rollingParser,
        MissingBolWorkbookParser missingBolParser,
        Func<string>? downloadsLocator = null)
    {
        _repository = repository;
        _missingBolRepository = missingBolRepository;
        _rollingParser = rollingParser;
        _missingBolParser = missingBolParser;
        _downloadsLocator = downloadsLocator ?? DownloadsLocator.GetDownloadsFolder;
    }

    public async Task<ReportUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(UpdateCore, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private ReportUpdateResult UpdateCore()
    {
        var downloads = _downloadsLocator();
        if (!Directory.Exists(downloads))
        {
            var rollingFailure = new ReportSourceUpdateResult(
                ReportSourceUpdateState.Failed,
                false,
                $"Downloads folder was not found: {downloads}",
                null,
                _repository.GetCurrentReportCycle());
            var bolFailure = _missingBolRepository is null
                ? NotConfiguredMissingBol()
                : new ReportSourceUpdateResult(
                    ReportSourceUpdateState.Failed,
                    _missingBolRepository.ClearCurrent(),
                    $"Downloads folder was not found: {downloads}; no Missing BOL rows shown",
                    null,
                    null);
            return Combine(rollingFailure, bolFailure);
        }

        var rolling = UpdateRollingSevenDay(downloads);
        var missingBol = UpdateMissingBol(downloads);
        return Combine(rolling, missingBol);
    }

    private ReportSourceUpdateResult UpdateRollingSevenDay(string downloads)
    {
        var paths = Directory
            .EnumerateFiles(downloads, "*.csv", SearchOption.TopDirectoryOnly)
            .Where(IsRollingSevenDayCandidate)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        if (paths.Length == 0)
        {
            return new ReportSourceUpdateResult(
                ReportSourceUpdateState.NotFound,
                false,
                "No rolling 7 day_data CSV found; saved roster preserved",
                null,
                _repository.GetCurrentReportCycle());
        }

        var validCandidates = new List<RollingCandidate>();
        var failures = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var bytes = ReadStableFile(path, out var lastWriteUtc);
                validCandidates.Add(new RollingCandidate(
                    path,
                    bytes,
                    lastWriteUtc,
                    _rollingParser.Parse(bytes)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ReportValidationException)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        if (validCandidates.Count == 0)
        {
            var detail = failures.Count > 0 ? failures[0] : "No candidate could be read.";
            return new ReportSourceUpdateResult(
                ReportSourceUpdateState.Failed,
                false,
                $"No valid report imported — {detail}; saved roster preserved",
                null,
                _repository.GetCurrentReportCycle());
        }

        var selected = validCandidates
            .OrderByDescending(candidate => candidate.Import.ReportCycleDate)
            .ThenByDescending(candidate => candidate.LastWriteUtc)
            .First();
        var currentCycle = _repository.GetCurrentReportCycle();
        if (currentCycle is not null && selected.Import.ReportCycleDate < currentCycle.Value)
        {
            return new ReportSourceUpdateResult(
                ReportSourceUpdateState.SkippedOlder,
                false,
                $"Newest valid cycle {selected.Import.ReportCycleDate:M/d/yyyy} is older than saved cycle {currentCycle.Value:M/d/yyyy}; nothing changed",
                Path.GetFileName(selected.Path),
                currentCycle);
        }

        var hash = Convert.ToHexString(SHA256.HashData(selected.Bytes));
        var result = _repository.ImportReport(
            selected.Import,
            Path.GetFileName(selected.Path),
            selected.Path,
            hash,
            selected.LastWriteUtc);
        var warning = failures.Count > 0
            ? $"; ignored {failures.Count.ToString(CultureInfo.InvariantCulture)} invalid candidate(s)"
            : string.Empty;

        return result.AlreadyAccepted
            ? new ReportSourceUpdateResult(
                ReportSourceUpdateState.Current,
                false,
                $"Already current for cycle {selected.Import.ReportCycleDate:M/d/yyyy}{warning}",
                Path.GetFileName(selected.Path),
                selected.Import.ReportCycleDate)
            : new ReportSourceUpdateResult(
                ReportSourceUpdateState.Imported,
                true,
                $"Updated {selected.Import.Drivers.Count.ToString(CultureInfo.InvariantCulture)} drivers from {Path.GetFileName(selected.Path)} — cycle {selected.Import.ReportCycleDate:M/d/yyyy}{warning}",
                Path.GetFileName(selected.Path),
                selected.Import.ReportCycleDate);
    }

    private ReportSourceUpdateResult UpdateMissingBol(string downloads)
    {
        if (_missingBolRepository is null || _missingBolParser is null)
        {
            return NotConfiguredMissingBol();
        }

        var paths = Directory
            .EnumerateFiles(downloads, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(IsMissingBolCandidate)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        if (paths.Length == 0)
        {
            var changed = _missingBolRepository.ClearCurrent();
            return new ReportSourceUpdateResult(
                ReportSourceUpdateState.NotFound,
                changed,
                "No Missing BOL workbook found; no Missing BOL rows shown",
                null,
                null);
        }

        var failures = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var bytes = ReadStableFile(path, out var lastWriteUtc);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (_missingBolRepository.IsHashAccepted(hash))
                {
                    var warning = failures.Count > 0
                        ? $"; ignored {failures.Count.ToString(CultureInfo.InvariantCulture)} newer invalid candidate(s)"
                        : string.Empty;
                    return new ReportSourceUpdateResult(
                        ReportSourceUpdateState.Current,
                        false,
                        $"Missing BOL view already matches {Path.GetFileName(path)}{warning}",
                        Path.GetFileName(path),
                        null);
                }

                var parsed = _missingBolParser.Parse(bytes);
                var result = _missingBolRepository.ImportWorkbook(
                    parsed,
                    Path.GetFileName(path),
                    path,
                    hash,
                    lastWriteUtc);
                var ignored = failures.Count > 0
                    ? $"; ignored {failures.Count.ToString(CultureInfo.InvariantCulture)} newer invalid candidate(s)"
                    : string.Empty;
                return new ReportSourceUpdateResult(
                    ReportSourceUpdateState.Imported,
                    result.Imported,
                    $"Showing {result.ItemCount.ToString(CultureInfo.InvariantCulture)} Missing BOL order(s) from {Path.GetFileName(path)} — read-only, not stored{ignored}",
                    Path.GetFileName(path),
                    null);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ReportValidationException or
                InvalidOperationException)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        var cleared = _missingBolRepository.ClearCurrent();
        var detail = failures.Count > 0 ? failures[0] : "No candidate could be read.";
        return new ReportSourceUpdateResult(
            ReportSourceUpdateState.Failed,
            cleared,
            $"Missing BOL view could not be loaded — {detail}; no Missing BOL rows shown",
            null,
            null);
    }

    private static ReportUpdateResult Combine(
        ReportSourceUpdateResult rolling,
        ReportSourceUpdateResult missingBol)
    {
        var prefix = rolling.Succeeded && missingBol.Succeeded
            ? string.Empty
            : "Partial update — ";
        return new ReportUpdateResult(
            rolling.Changed || missingBol.Changed,
            $"{prefix}Rolling 7 Day: {rolling.Message}  •  Missing BOL: {missingBol.Message}",
            rolling.SourceFile ?? missingBol.SourceFile,
            rolling.ReportCycleDate,
            rolling,
            missingBol);
    }

    private static ReportSourceUpdateResult NotConfiguredMissingBol() =>
        new(
            ReportSourceUpdateState.NotConfigured,
            false,
            "not configured in this host",
            null,
            null);

    internal static bool IsRollingSevenDayCandidate(string path)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        return fileNameWithoutExtension.StartsWith("rolling 7 day_data", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsMissingBolCandidate(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        return fileNameWithoutExtension.StartsWith(
            "Order Details Missing BOL",
            StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadStableFile(string path, out DateTime lastWriteUtc)
    {
        var before = new FileInfo(path);
        before.Refresh();
        if (before.Length <= 0)
        {
            throw new ReportValidationException("The file is empty.");
        }

        if (before.Length > MaximumReportBytes)
        {
            throw new ReportValidationException("The file is unexpectedly larger than 100 MB.");
        }

        byte[] bytes;
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   64 * 1024,
                   FileOptions.SequentialScan))
        {
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }

        var after = new FileInfo(path);
        after.Refresh();
        if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            throw new IOException(
                "The file changed while WAA was reading it. Try Update Reports again after the download finishes.");
        }

        lastWriteUtc = after.LastWriteTimeUtc;
        return bytes;
    }

    private sealed record RollingCandidate(
        string Path,
        byte[] Bytes,
        DateTime LastWriteUtc,
        RollingSevenDayImport Import);
}
