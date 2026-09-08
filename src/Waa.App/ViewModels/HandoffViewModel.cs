using System.Collections.ObjectModel;
using System.Globalization;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;

namespace Waa.App.ViewModels;

public sealed class HandoffViewModel : ObservableObject
{
    private readonly WaaRepository _repository;
    private readonly MissingBolRepository? _missingBolRepository;
    private readonly HandoffService _handoffService;
    private readonly IClipboardService _clipboardService;
    private readonly Action<string> _reportStatus;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _timeZone;
    private string _draftText = string.Empty;
    private string _summaryText = "Handoff has not been generated.";
    private bool _isBusy;
    private bool _hasGenerated;

    public HandoffViewModel(
        WaaRepository repository,
        HandoffService handoffService,
        IClipboardService clipboardService,
        Action<string> reportStatus,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null,
        MissingBolRepository? missingBolRepository = null)
    {
        _repository = repository;
        _missingBolRepository = missingBolRepository;
        _handoffService = handoffService;
        _clipboardService = clipboardService;
        _reportStatus = reportStatus;
        _now = now ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;

        RegenerateCommand = new AsyncRelayCommand(RegenerateAsync, () => !IsBusy);
        CopyCommand = new AsyncRelayCommand(CopyAsync, () => !IsBusy && DraftText.Length > 0);
        DismissWorkedItemCommand = new AsyncRelayCommand<HandoffWorkedItemViewModel>(
  DismissWorkedItemAsync,
  item => !IsBusy && item is not null);
    }

    public ObservableCollection<HandoffWorkedItemViewModel> WorkedItems { get; } = new();
    public AsyncRelayCommand RegenerateCommand { get; }
    public AsyncRelayCommand CopyCommand { get; }
    public AsyncRelayCommand<HandoffWorkedItemViewModel> DismissWorkedItemCommand { get; }
    public bool HasGenerated => _hasGenerated;
    public bool HasWorkedItems => WorkedItems.Count > 0;

    public string DraftText
    {
        get => _draftText;
        set
        {
            if (SetProperty(ref _draftText, value))
            {
                CopyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RegenerateCommand.RaiseCanExecuteChanged();
                CopyCommand.RaiseCanExecuteChanged();
                DismissWorkedItemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Task OpenAsync() => _hasGenerated ? Task.CompletedTask : RegenerateAsync();

    public async Task RegenerateAsync()
    {
        try
        {
            IsBusy = true;
            var day = LocalDayRange.Create(_now(), _timeZone);
            var loaded = await Task.Run(() =>
            {
                var fleet = _repository.LoadFleet();
                var saved = _repository.LoadHandoffEntries(day.StartUtc, day.EndUtc);
                var classified = _missingBolRepository?.ApplyWorkSources(saved) ?? saved;
                var currentWork = classified
                    .Where(entry => entry.Source is not WorkEntrySource.MissingBolTask and not WorkEntrySource.MissingBolAction)
                    .ToArray();
                var currentBol = _missingBolRepository?.BuildCurrentHandoffEntries(fleet.Drivers)
                    ?? Array.Empty<WorkEntryRecord>();
                return (
                    Entries: currentWork.Concat(currentBol).ToArray(),
                    CurrentWork: currentWork,
                    Drivers: fleet.Drivers);
            });
            var result = _handoffService.Generate(
                loaded.Entries,
                loaded.Drivers,
                day);
            DraftText = result.Text;
            ReplaceWorkedItems(loaded.CurrentWork, loaded.Drivers, day);
            SummaryText =
                $"{result.DriverLineCount} driver notes  •  " +
                $"{result.MissingBolDriverCount} drivers with Missing BOL  •  " +
                $"{result.MissingBolOrderCount} BOL orders in current file";
            _hasGenerated = true;
            OnPropertyChanged(nameof(HasGenerated));
            _reportStatus($"Handoff regenerated from saved work and the current Missing BOL workbook for {day.LocalDate:M/d/yyyy}.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Handoff generation failed");
            _reportStatus($"Handoff could not be generated: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CopyAsync()
    {
        try
        {
            _clipboardService.SetText(DraftText);
            _reportStatus("Current handoff text copied to the Windows clipboard.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Handoff clipboard copy failed");
            _reportStatus($"Handoff could not be copied: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task DismissWorkedItemAsync(HandoffWorkedItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var changed = false;
        try
        {
            IsBusy = true;
            changed = await Task.Run(() => _repository.DismissCompletedWorkFromHandoff(item.WorkEntryId));
            if (!changed)
            {
                _reportStatus("That item is not eligible to be removed from Handoff, or it was already removed.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Handoff completed-work dismissal failed");
            _reportStatus($"The worked item could not be removed from Handoff: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        if (!changed)
        {
            return;
        }

        await RegenerateAsync();
        if (WorkedItems.Any(worked => worked.WorkEntryId == item.WorkEntryId))
        {
            _reportStatus(
                $"Removed completed Handoff item for {item.DriverName} from future regenerations, " +
                "but the draft could not be refreshed. Work history was preserved.");
            return;
        }

        _reportStatus($"Removed completed Handoff item for {item.DriverName}. Work history was preserved.");
    }

    private void ReplaceWorkedItems(
        IReadOnlyCollection<WorkEntryRecord> workEntries,
        IReadOnlyCollection<FleetDriverRecord> currentDrivers,
        LocalDayRange day)
    {
        var currentByCode = currentDrivers
  .GroupBy(driver => driver.DriverCode, StringComparer.OrdinalIgnoreCase)
  .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var completed = workEntries
  .Where(entry =>
      (entry.Status == WorkEntryStatus.Done && day.Contains(entry.CreatedUtc)) ||
      (entry.ResolvedUtc is { } resolvedUtc && day.Contains(resolvedUtc)))
  .OrderByDescending(entry => entry.ResolvedUtc ?? entry.CreatedUtc)
  .ThenBy(entry => entry.DriverName, StringComparer.OrdinalIgnoreCase)
  .ThenBy(entry => entry.Id)
  .ToArray();

        WorkedItems.Clear();
        foreach (var entry in completed)
        {
            currentByCode.TryGetValue(entry.DriverCode, out var currentDriver);
            WorkedItems.Add(new HandoffWorkedItemViewModel(entry, currentDriver, _timeZone));
        }

        OnPropertyChanged(nameof(HasWorkedItems));
    }
}

public sealed class HandoffWorkedItemViewModel
{
    public HandoffWorkedItemViewModel(
        WorkEntryRecord record,
        FleetDriverRecord? currentDriver,
        TimeZoneInfo timeZone)
    {
        Record = record;
        var unitCode = IsMeaningful(currentDriver?.UnitCode)
  ? currentDriver!.UnitCode.Trim()
  : IsMeaningful(record.UnitCodeSnapshot)
      ? record.UnitCodeSnapshot.Trim()
      : string.Empty;
        DriverName = currentDriver?.DriverName ?? record.DriverName;
        IdentityDisplay = unitCode.Length == 0
  ? $"{DriverName} [{record.DriverCode}]"
  : $"{unitCode} — {DriverName} [{record.DriverCode}]";
        TextDisplay = CollapseWhitespace(record.Text);
        var completedUtc = record.ResolvedUtc ?? record.CreatedUtc;
        CompletedDisplay = TimeZoneInfo.ConvertTime(completedUtc, timeZone)
  .ToString("g", CultureInfo.CurrentCulture);
    }

    public WorkEntryRecord Record { get; }
    public long WorkEntryId => Record.Id;
    public string DriverName { get; }
    public string IdentityDisplay { get; }
    public string TextDisplay { get; }
    public string CompletedDisplay { get; }

    private static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim() != "*";

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
