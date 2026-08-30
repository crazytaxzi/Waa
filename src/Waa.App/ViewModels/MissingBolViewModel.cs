using System.Collections.ObjectModel;
using System.Globalization;
using Waa.App.Data;
using Waa.App.Infrastructure;

namespace Waa.App.ViewModels;

public sealed class MissingBolViewModel : ObservableObject
{
    private readonly MissingBolRepository _repository;
    private readonly Func<string, Task> _onStateChanged;
    private readonly Action<string> _reportStatus;
    private FleetDriverRecord? _driver;
    private string _summaryText = "Select a driver to review Missing BOL work.";
    private bool _isBusy;
    private int _loadVersion;

    public MissingBolViewModel(
        MissingBolRepository repository,
        Func<string, Task> onStateChanged,
        Action<string> reportStatus)
    {
        _repository = repository;
        _onStateChanged = onStateChanged;
        _reportStatus = reportStatus;
    }

    public ObservableCollection<MissingBolItemViewModel> Items { get; } = new();

    public bool HasDriver => _driver is not null;
    public bool HasItems => Items.Count > 0;

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task SetDriverAsync(FleetDriverRecord? driver)
    {
        _driver = driver;
        OnPropertyChanged(nameof(HasDriver));
        var version = ++_loadVersion;
        if (driver is null)
        {
            Items.Clear();
            OnPropertyChanged(nameof(HasItems));
            SummaryText = "Select a driver to review Missing BOL work.";
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
            var records = await Task.Run(() => _repository.LoadDriverItems(driver.DriverCode));
            if (version != _loadVersion ||
                _driver is null ||
                !_driver.DriverCode.Equals(driver.DriverCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Items.Clear();
            foreach (var record in records)
            {
                Items.Add(new MissingBolItemViewModel(record, SaveActionAsync));
            }

            OnPropertyChanged(nameof(HasItems));
            var open = records.Count(item => !item.IsResolved);
            var resolved = records.Count - open;
            SummaryText = records.Count == 0
                ? "No Missing BOL items for this driver."
                : $"{open.ToString(CultureInfo.CurrentCulture)} open" +
                  (resolved > 0
                      ? $"  •  {resolved.ToString(CultureInfo.CurrentCulture)} resolved"
                      : string.Empty);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Selected driver Missing BOL load failed");
            _reportStatus($"Missing BOL work could not be loaded: {exception.Message}");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private async Task<bool> SaveActionAsync(
        MissingBolItemViewModel item,
        MissingBolActionOutcome outcome,
        string note)
    {
        var driver = _driver;
        if (driver is null)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await Task.Run(() => _repository.RecordAction(item.Record.Id, outcome, note));
            await _onStateChanged(driver.DriverCode);
            if (_driver is not null &&
                _driver.DriverCode.Equals(driver.DriverCode, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshAsync();
            }

            _reportStatus($"{ActionDisplay(outcome)} saved for Missing BOL order {item.OrderNumber}.");
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Missing BOL action save failed");
            _reportStatus($"Missing BOL action was not saved: {exception.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ActionDisplay(MissingBolActionOutcome outcome) => outcome switch
    {
        MissingBolActionOutcome.FollowUp => "Follow-up",
        _ => outcome.ToString()
    };
}

public sealed class MissingBolItemViewModel : ObservableObject
{
    private readonly Func<MissingBolItemViewModel, MissingBolActionOutcome, string, Task<bool>> _save;
    private string _note = string.Empty;
    private bool _isSaving;

    public MissingBolItemViewModel(
        MissingBolItemRecord record,
        Func<MissingBolItemViewModel, MissingBolActionOutcome, string, Task<bool>> save)
    {
        Record = record;
        _save = save;
        RequestedCommand = CreateCommand(MissingBolActionOutcome.Requested, () => !IsResolved);
        AttemptedCommand = CreateCommand(MissingBolActionOutcome.Attempted, () => !IsResolved);
        FollowUpCommand = CreateCommand(MissingBolActionOutcome.FollowUp, () => !IsResolved);
        ResolvedCommand = CreateCommand(MissingBolActionOutcome.Resolved, () => !IsResolved);
        ReopenCommand = CreateCommand(MissingBolActionOutcome.Reopen, () => IsResolved);
    }

    public MissingBolItemRecord Record { get; }
    public AsyncRelayCommand RequestedCommand { get; }
    public AsyncRelayCommand AttemptedCommand { get; }
    public AsyncRelayCommand FollowUpCommand { get; }
    public AsyncRelayCommand ResolvedCommand { get; }
    public AsyncRelayCommand ReopenCommand { get; }

    public string OrderNumber => Record.SourceOrderNumber;
    public string EmptyCallDateDisplay => Record.EmptyCallDate.ToString("M/d/yyyy", CultureInfo.CurrentCulture);
    public string StatusDisplay => Record.CurrentStatus == MissingBolStatus.FollowUp
        ? "Follow-up"
        : Record.CurrentStatus.ToString();
    public bool IsResolved => Record.IsResolved;
    public string RouteDisplay => FormatRoute(Record.OriginCityState, Record.DestinationCityState);
    public string CustomerDisplay => Record.BillTo.Length == 0
        ? string.Empty
        : $"Customer: {Record.BillTo}";
    public string MilesDisplay => FormatMiles(Record.LoadedMiles, Record.OrderLevelMiles);
    public string SourceEvidence => FormatSourceEvidence(
        Record.SourceDriverCode,
        Record.SourceDriverName);
    public string PresenceWarning =>
        Record.IsResolved && Record.ReturnedAfterResolution && Record.IsPresentInLatestImport
            ? "Resolved — present again in latest report"
            : Record.IsPresentInLatestImport
                ? string.Empty
                : "Not in latest report";
    public bool HasPresenceWarning => PresenceWarning.Length > 0;
    public string NameWarning => Record.SourceNameDiffersFromDriver
        ? $"Exact Driver Code matched; source name “{Record.SourceDriverName}” differs from WAA name “{Record.MatchedDriverName}”."
        : string.Empty;
    public bool HasNameWarning => NameWarning.Length > 0;

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                RequestedCommand.RaiseCanExecuteChanged();
                AttemptedCommand.RaiseCanExecuteChanged();
                FollowUpCommand.RaiseCanExecuteChanged();
                ResolvedCommand.RaiseCanExecuteChanged();
                ReopenCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private AsyncRelayCommand CreateCommand(
        MissingBolActionOutcome outcome,
        Func<bool> statusAllows) =>
        new(
            () => SaveAsync(outcome),
            () => !IsSaving && statusAllows());

    private async Task SaveAsync(MissingBolActionOutcome outcome)
    {
        var note = Note;
        try
        {
            IsSaving = true;
            if (await _save(this, outcome, note))
            {
                Note = string.Empty;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string FormatRoute(string origin, string destination)
    {
        if (origin.Length > 0 && destination.Length > 0)
        {
            return $"{origin} → {destination}";
        }

        if (origin.Length > 0)
        {
            return $"Origin: {origin}";
        }

        if (destination.Length > 0)
        {
            return $"Destination: {destination}";
        }

        return "Route not supplied";
    }

    private static string FormatMiles(decimal? loadedMiles, decimal? orderMiles)
    {
        if (loadedMiles is null && orderMiles is null)
        {
            return string.Empty;
        }

        if (loadedMiles is not null && orderMiles is not null)
        {
            return $"Miles: {loadedMiles.Value:0.##} loaded / {orderMiles.Value:0.##} order";
        }

        return loadedMiles is not null
            ? $"Loaded miles: {loadedMiles.Value:0.##}"
            : $"Order miles: {orderMiles!.Value:0.##}";
    }

    private static string FormatSourceEvidence(string code, string name)
    {
        var displayCode = code.Length == 0 ? "(blank)" : code;
        return name.Length == 0
            ? $"Source Driver Code: {displayCode}"
            : $"Source Driver Code: {displayCode}  •  {name}";
    }
}

public sealed class MissingBolUnmatchedItemViewModel
{
    public MissingBolUnmatchedItemViewModel(MissingBolUnmatchedRecord record)
    {
        Record = record;
    }

    public MissingBolUnmatchedRecord Record { get; }
    public string OrderNumber => Record.SourceOrderNumber;
    public string EmptyCallDateDisplay => Record.EmptyCallDate.ToString("M/d/yyyy", CultureInfo.CurrentCulture);
    public string SourceDriverCodeDisplay => Record.SourceDriverCode.Length == 0
        ? "(blank)"
        : Record.SourceDriverCode;
    public string SourceDriverName => Record.SourceDriverName;
    public string RouteDisplay => Record.OriginCityState.Length > 0 && Record.DestinationCityState.Length > 0
        ? $"{Record.OriginCityState} → {Record.DestinationCityState}"
        : Record.OriginCityState.Length > 0
            ? $"Origin: {Record.OriginCityState}"
            : Record.DestinationCityState.Length > 0
                ? $"Destination: {Record.DestinationCityState}"
                : "Route not supplied";
    public string PresenceDisplay => Record.IsPresentInLatestImport
        ? string.Empty
        : "Not in latest report";
}
