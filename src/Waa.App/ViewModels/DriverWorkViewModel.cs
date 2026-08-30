using System.Collections.ObjectModel;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;

namespace Waa.App.ViewModels;

public sealed class DriverWorkViewModel : ObservableObject
{
    private readonly WaaRepository _repository;
    private readonly MissingBolRepository? _missingBolRepository;
    private readonly Func<string, Task> _onWorkChanged;
    private readonly Action<string> _reportStatus;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _timeZone;
    private readonly Dictionary<string, string> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private FleetDriverRecord? _driver;
    private string _newWorkText = string.Empty;
    private string _openWorkSummary = "Select a driver to view open work.";
    private string _todaySummary = "Select a driver to view today’s activity.";
    private bool _isBusy;
    private int _loadVersion;

    public DriverWorkViewModel(
        WaaRepository repository,
        Func<string, Task> onWorkChanged,
        Action<string> reportStatus,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null,
        MissingBolRepository? missingBolRepository = null)
    {
        _repository = repository;
        _missingBolRepository = missingBolRepository;
        _onWorkChanged = onWorkChanged;
        _reportStatus = reportStatus;
        _now = now ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;

        SaveDoneCommand = new AsyncRelayCommand(
            () => SaveAsync(WorkEntryStatus.Done),
            CanSave);
        SaveWaitingCommand = new AsyncRelayCommand(
            () => SaveAsync(WorkEntryStatus.Waiting),
            CanSave);
        SaveFollowUpCommand = new AsyncRelayCommand(
            () => SaveAsync(WorkEntryStatus.FollowUp),
            CanSave);
    }

    public ObservableCollection<WorkEntryItemViewModel> OpenEntries { get; } = new();
    public ObservableCollection<WorkEntryItemViewModel> TodayEntries { get; } = new();
    public AsyncRelayCommand SaveDoneCommand { get; }
    public AsyncRelayCommand SaveWaitingCommand { get; }
    public AsyncRelayCommand SaveFollowUpCommand { get; }

    public bool HasDriver => _driver is not null;

    public string NewWorkText
    {
        get => _newWorkText;
        set
        {
            if (SetProperty(ref _newWorkText, value))
            {
                if (_driver is not null)
                {
                    _drafts[_driver.DriverCode] = value;
                }

                RefreshCommandStates();
            }
        }
    }

    public string OpenWorkSummary
    {
        get => _openWorkSummary;
        private set => SetProperty(ref _openWorkSummary, value);
    }

    public string TodaySummary
    {
        get => _todaySummary;
        private set => SetProperty(ref _todaySummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public async Task SetDriverAsync(FleetDriverRecord? driver)
    {
        var version = ++_loadVersion;
        if (_driver is not null)
        {
            _drafts[_driver.DriverCode] = NewWorkText;
        }

        _driver = driver;
        OnPropertyChanged(nameof(HasDriver));
        NewWorkText = driver is not null && _drafts.TryGetValue(driver.DriverCode, out var draft)
            ? draft
            : string.Empty;

        if (driver is null)
        {
            OpenEntries.Clear();
            TodayEntries.Clear();
            OpenWorkSummary = "Select a driver to view open work.";
            TodaySummary = "Select a driver to view today’s activity.";
            RefreshCommandStates();
            return;
        }

        await LoadAsync(driver, version);
    }

    public async Task RefreshAsync()
    {
        var driver = _driver;
        if (driver is null)
        {
            return;
        }

        await LoadAsync(driver, ++_loadVersion);
    }

    private async Task LoadAsync(FleetDriverRecord driver, int version)
    {
        try
        {
            IsBusy = true;
            var day = LocalDayRange.Create(_now(), _timeZone);
            var state = await Task.Run(() => _repository.LoadDriverWork(
                driver.DriverCode,
                day.StartUtc,
                day.EndUtc));
            var openEntries = _missingBolRepository?.ApplyWorkSources(state.OpenEntries)
                ?? state.OpenEntries;
            var todayEntries = (_missingBolRepository?.ApplyWorkSources(state.TodayEntries)
                    ?? state.TodayEntries)
                .Where(entry => entry.Source != WorkEntrySource.MissingBolTask)
                .ToArray();

            if (version != _loadVersion ||
                _driver is null ||
                !_driver.DriverCode.Equals(driver.DriverCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReplaceItems(OpenEntries, openEntries);
            ReplaceItems(TodayEntries, todayEntries);
            OpenWorkSummary = openEntries.Count == 0
                ? "No unresolved work."
                : $"{openEntries.Count} unresolved item{(openEntries.Count == 1 ? string.Empty : "s")}.";
            TodaySummary = todayEntries.Length == 0
                ? "No activity recorded today."
                : $"{todayEntries.Length} activit{(todayEntries.Length == 1 ? "y" : "ies")} today — newest first.";
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Driver work load failed");
            _reportStatus($"Driver work could not be loaded: {exception.Message}");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private async Task SaveAsync(WorkEntryStatus status)
    {
        var driver = _driver;
        var text = NewWorkText;
        if (driver is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await Task.Run(() => _repository.RecordManualWork(driver, status, text));
            _drafts.Remove(driver.DriverCode);
            NewWorkText = string.Empty;
            await _onWorkChanged(driver.DriverCode);
            await RefreshAsync();
            var statusText = status == WorkEntryStatus.FollowUp ? "Follow-up" : status.ToString();
            _reportStatus($"{statusText} work saved for {driver.DriverName}.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Manual work save failed");
            NewWorkText = text;
            _drafts[driver.DriverCode] = text;
            _reportStatus($"Work was not saved: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResolveAsync(long workEntryId)
    {
        var driver = _driver;
        if (driver is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var changed = await Task.Run(() => _repository.ResolveWorkEntry(workEntryId));
            if (!changed)
            {
                _reportStatus("That work item was already resolved or could not be found.");
                return;
            }

            await _onWorkChanged(driver.DriverCode);
            await RefreshAsync();
            _reportStatus($"Work resolved for {driver.DriverName}.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Work resolve failed");
            _reportStatus($"Work was not resolved: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReopenAsync(long workEntryId)
    {
        var driver = _driver;
        if (driver is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var changed = await Task.Run(() => _repository.ReopenWorkEntry(workEntryId));
            if (!changed)
            {
                _reportStatus("That work item was already open or could not be found.");
                return;
            }

            await _onWorkChanged(driver.DriverCode);
            await RefreshAsync();
            _reportStatus($"Work reopened for {driver.DriverName}.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Work reopen failed");
            _reportStatus($"Work was not reopened: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceItems(
        ObservableCollection<WorkEntryItemViewModel> target,
        IEnumerable<WorkEntryRecord> records)
    {
        target.Clear();
        foreach (var record in records)
        {
            target.Add(new WorkEntryItemViewModel(
                record,
                ResolveAsync,
                ReopenAsync,
                _timeZone));
        }
    }

    private bool CanSave() =>
        !IsBusy && _driver is not null && !string.IsNullOrWhiteSpace(NewWorkText);

    private void RefreshCommandStates()
    {
        SaveDoneCommand.RaiseCanExecuteChanged();
        SaveWaitingCommand.RaiseCanExecuteChanged();
        SaveFollowUpCommand.RaiseCanExecuteChanged();
    }
}
