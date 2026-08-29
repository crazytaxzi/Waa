using System.Globalization;
using System.Security.Cryptography;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.Core;

namespace Waa.App.Services;

public sealed record ReportUpdateResult(
    bool Changed,
    string Message,
    string? SourceFile,
    DateOnly? ReportCycleDate);

public sealed class ReportUpdateService
{
    private const long MaximumReportBytes = 100L * 1024L * 1024L;
    private readonly WaaRepository _repository;
    private readonly RollingSevenDayCsvParser _parser;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ReportUpdateService(WaaRepository repository, RollingSevenDayCsvParser parser)
    {
        _repository = repository;
        _parser = parser;
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
        var downloads = DownloadsLocator.GetDownloadsFolder();
        if (!Directory.Exists(downloads))
        {
            return new ReportUpdateResult(false, $"Downloads folder was not found: {downloads}", null, null);
        }

        var paths = Directory
            .EnumerateFiles(downloads, "*.csv", SearchOption.TopDirectoryOnly)
            .Where(IsRollingSevenDayCandidate)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        if (paths.Length == 0)
        {
            return new ReportUpdateResult(
                false,
                "No rolling 7 day_data CSV was found in Downloads. The saved roster was left unchanged.",
                null,
                _repository.GetCurrentReportCycle());
        }

        var validCandidates = new List<ParsedCandidate>();
        var failures = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var bytes = ReadStableFile(path, out var lastWriteUtc);
                var parsed = _parser.Parse(bytes);
                validCandidates.Add(new ParsedCandidate(path, bytes, lastWriteUtc, parsed));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ReportValidationException)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        if (validCandidates.Count == 0)
        {
            var detail = failures.Count > 0 ? failures[0] : "No candidate could be read.";
            return new ReportUpdateResult(
                false,
                $"No valid Rolling 7 Day report was imported. {detail} The saved roster was left unchanged.",
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
            return new ReportUpdateResult(
                false,
                $"The newest valid report in Downloads is cycle {selected.Import.ReportCycleDate:M/d/yyyy}, older than the saved cycle {currentCycle.Value:M/d/yyyy}. Nothing changed.",
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
            ? $" Ignored {failures.Count.ToString(CultureInfo.InvariantCulture)} invalid candidate file(s)."
            : string.Empty;

        if (result.AlreadyAccepted)
        {
            return new ReportUpdateResult(
                false,
                $"Reports are already current for cycle {selected.Import.ReportCycleDate:M/d/yyyy}.{warning}",
                Path.GetFileName(selected.Path),
                selected.Import.ReportCycleDate);
        }

        return new ReportUpdateResult(
            true,
            $"Updated {selected.Import.Drivers.Count.ToString(CultureInfo.InvariantCulture)} drivers from {Path.GetFileName(selected.Path)} — cycle {selected.Import.ReportCycleDate:M/d/yyyy}.{warning}",
            Path.GetFileName(selected.Path),
            selected.Import.ReportCycleDate);
    }

    private static bool IsRollingSevenDayCandidate(string path)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        return fileNameWithoutExtension.StartsWith("rolling 7 day_data", StringComparison.OrdinalIgnoreCase);
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
            throw new IOException("The file changed while WAA was reading it. Try Update Reports again after the download finishes.");
        }

        lastWriteUtc = after.LastWriteTimeUtc;
        return bytes;
    }

    private sealed record ParsedCandidate(
        string Path,
        byte[] Bytes,
        DateTime LastWriteUtc,
        RollingSevenDayImport Import);
}
