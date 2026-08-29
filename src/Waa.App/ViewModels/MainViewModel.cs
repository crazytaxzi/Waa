using System.Collections.ObjectModel;
using System.Globalization;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;

namespace Waa.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly WaaRepository _repository;
    private readonly ReportUpdateService _updateService;
    private IReadOnlyList<FleetDriverRecord> _records = Array.Empty<FleetDriverRecord>();
    private decimal _threshold = 50m;
    private DriverRowViewModel? _selectedDriver;
    private string _searchText = string.Empty;
    private string _thresholdText = "50.0";
    private string _contactNote = string.Empty;
    private string _reportCycleText = "No report";
    private string _fleet7DayText = "N/A";
    private string _fleet28DayText = "N/A";
    private string _contactProgressText = "0 need contact";
    private string _statusMessage = "Starting WAA…";
    private string _rosterSummaryText = "No roster loaded";
    private bool _isBusy;
    private bool _initialized;

    public MainViewModel(WaaRepository repository, ReportUpdateService updateService)
    {
        _repository = repository;
        _updateService = updateService;

        UpdateReportsCommand = new AsyncRelayCommand(
            () => UpdateReportsAsync(isLaunchUpdate: false),
            () => !IsBusy);
        ApplyThresholdCommand = new AsyncRelayCommand(ApplyThresholdAsync, () => !IsBusy);
        SpokeCommand = new AsyncRelayCommand(
            () => RecordContactAsync(IdleContactOutcome.Spoke),
            CanRecordContact);
        AttemptedCommand = new AsyncRelayCommand(
            () => RecordContactAsync(IdleContactOutcome.Attempted),
            CanRecordContact);
        FollowUpCommand = new AsyncRelayCommand(
            () => RecordContactAsync(IdleContactOutcome.SpokeFollowUp),
            CanRecordContact);
    }

    public ObservableCollection<DriverRowViewModel> Drivers { get; } = new();
    public AsyncRelayCommand UpdateReportsCommand { get; }
    public AsyncRelayCommand ApplyThresholdCommand { get; }
    public AsyncRelayCommand SpokeCommand { get; }
    public AsyncRelayCommand AttemptedCommand { get; }
    public AsyncRelayCommand FollowUpCommand { get; }

    public DriverRowViewModel? SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            if (SetProperty(ref _selectedDriver, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildVisibleRows(SelectedDriver?.DriverCode);
            }
        }
    }

    public string ThresholdText
    {
        get => _thresholdText;
        set => SetProperty(ref _thresholdText, value);
    }

    public string ContactNote
    {
        get => _contactNote;
        set => SetProperty(ref _contactNote, value);
    }

    public string ReportCycleText
    {
        get => _reportCycleText;
        private set => SetProperty(ref _reportCycleText, value);
    }

    public string Fleet7DayText
    {
        get => _fleet7DayText;
        private set => SetProperty(ref _fleet7DayText, value);
    }

    public string Fleet28DayText
    {
        get => _fleet28DayText;
        private set => SetProperty(ref _fleet28DayText, value);
    }

    public string ContactProgressText
    {
        get => _contactProgressText;
        private set => SetProperty(ref _contactProgressText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string RosterSummaryText
    {
        get => _rosterSummaryText;
        private set => SetProperty(ref _rosterSummaryText, value);
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

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            IsBusy = true;
            StatusMessage = "Loading saved roster…";
            await Task.Run(_repository.Initialize);
            _threshold = await Task.Run(_repository.GetIdleThreshold);
            ThresholdText = _threshold.ToString("0.0", CultureInfo.CurrentCulture);
            await ReloadFleetAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Initialization failed");
            StatusMessage = $"WAA could not initialize: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await UpdateReportsAsync(isLaunchUpdate: true);
    }

    private async Task UpdateReportsAsync(bool isLaunchUpdate)
    {
        if (IsBusy)
        {
            return;
        }

        var preserveCode = SelectedDriver?.DriverCode;
        try
        {
            IsBusy = true;
            StatusMessage = isLaunchUpdate
                ? "Checking Downloads once for the launch report update…"
                : "Updating reports from Downloads…";

            var result = await _updateService.UpdateAsync();
            await ReloadFleetAsync(preserveCode);
            StatusMessage = result.Message;
            AppLog.Write(result.Message);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Report update failed");
            StatusMessage = $"Report update failed: {exception.Message}. The saved roster was left unchanged.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyThresholdAsync()
    {
        if (!TryParseThreshold(ThresholdText, out var threshold) || threshold is < 0m or > 100m)
        {
            StatusMessage = "Idle threshold must be a number from 0 through 100.";
            return;
        }

        try
        {
            IsBusy = true;
            await Task.Run(() => _repository.SetIdleThreshold(threshold));
            _threshold = threshold;
            ThresholdText = threshold.ToString("0.0", CultureInfo.CurrentCulture);
            RebuildVisibleRows(SelectedDriver?.DriverCode);
            StatusMessage = $"Idle threshold changed to {threshold.ToString("0.0", CultureInfo.CurrentCulture)}%.";
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Threshold update failed");
            StatusMessage = $"Threshold was not changed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecordContactAsync(IdleContactOutcome outcome)
    {
        var selected = SelectedDriver;
        if (selected is null)
        {
            return;
        }

        var currentCode = selected.DriverCode;
        try
        {
            IsBusy = true;
            await Task.Run(() => _repository.RecordIdleContact(
                selected.Record,
                outcome,
                ContactNote,
                _threshold));
            ContactNote = string.Empty;
            await ReloadFleetAsync(currentCode);

            var next = Drivers.FirstOrDefault(driver =>
                driver.NeedsIdleAttention &&
                !driver.DriverCode.Equals(currentCode, StringComparison.OrdinalIgnoreCase));
            SelectedDriver = next ?? Drivers.FirstOrDefault(driver =>
                driver.DriverCode.Equals(currentCode, StringComparison.OrdinalIgnoreCase));

            var outcomeText = outcome switch
            {
                IdleContactOutcome.Spoke => "Spoke",
                IdleContactOutcome.Attempted => "Attempted",
                IdleContactOutcome.SpokeFollowUp => "Spoke — Follow-up",
                _ => outcome.ToString()
            };
            StatusMessage = $"{outcomeText} saved for {selected.DriverName} for cycle {selected.Record.ReportCycleDate:M/d/yyyy}.";
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Idle contact save failed");
            StatusMessage = $"Idle contact was not saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadFleetAsync(string? preferredDriverCode = null)
    {
        var state = await Task.Run(_repository.LoadFleet);
        _records = state.Drivers;
        ReportCycleText = state.ReportCycleDate?.ToString("M/d/yyyy", CultureInfo.CurrentCulture) ?? "No report";
        Fleet7DayText = FormatFleetPercent(
            state.FleetIdlePercent7Day,
            state.IncludedDrivers7Day,
            state.Drivers.Count);
        Fleet28DayText = FormatFleetPercent(
            state.FleetIdlePercent28Day,
            state.IncludedDrivers28Day,
            state.Drivers.Count);

        if (state.LastImportedUtc is null)
        {
            RosterSummaryText = state.Drivers.Count == 0
                ? "No roster loaded"
                : $"{state.Drivers.Count.ToString(CultureInfo.CurrentCulture)} drivers";
        }
        else
        {
            var local = state.LastImportedUtc.Value.ToLocalTime();
            RosterSummaryText = $"{state.Drivers.Count.ToString(CultureInfo.CurrentCulture)} drivers  •  {state.LastImportFile}  •  {local:g}";
        }

        RebuildVisibleRows(preferredDriverCode);
    }

    private void RebuildVisibleRows(string? preferredDriverCode)
    {
        var previousCode = preferredDriverCode ?? SelectedDriver?.DriverCode;
        var query = _records
            .Select(record => new DriverRowViewModel(record, _threshold))
            .Where(MatchesSearch)
            .OrderBy(driver => driver.PriorityBand)
            .ThenBy(driver => driver.AttentionRank)
            .ThenByDescending(driver => driver.ConcernPercent ?? decimal.MinValue)
            .ThenBy(driver => driver.DriverName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(driver => driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Drivers.Clear();
        foreach (var driver in query)
        {
            Drivers.Add(driver);
        }

        SelectedDriver = previousCode is null
            ? Drivers.FirstOrDefault()
            : Drivers.FirstOrDefault(driver => driver.DriverCode.Equals(previousCode, StringComparison.OrdinalIgnoreCase))
              ?? Drivers.FirstOrDefault();

        var allRows = _records.Select(record => new DriverRowViewModel(record, _threshold)).ToArray();
        var needContact = allRows.Count(driver => driver.NeedsIdleAttention);
        var aboveThreshold = allRows.Count(driver => driver.IsAboveThreshold);
        var spoken = allRows.Count(driver =>
            driver.IsAboveThreshold &&
            driver.Record.LatestOutcome is IdleContactOutcome.Spoke or IdleContactOutcome.SpokeFollowUp);

        ContactProgressText = $"{needContact.ToString(CultureInfo.CurrentCulture)} need  •  {spoken.ToString(CultureInfo.CurrentCulture)}/{aboveThreshold.ToString(CultureInfo.CurrentCulture)} spoken";
        RefreshCommandStates();
    }

    private bool MatchesSearch(DriverRowViewModel driver)
    {
        var search = SearchText.Trim();
        if (search.Length == 0)
        {
            return true;
        }

        return driver.DriverCode.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               driver.DriverName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               driver.UnitCode.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               driver.DriverLeader.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool CanRecordContact() => !IsBusy && SelectedDriver is not null;

    private void RefreshCommandStates()
    {
        UpdateReportsCommand.RaiseCanExecuteChanged();
        ApplyThresholdCommand.RaiseCanExecuteChanged();
        SpokeCommand.RaiseCanExecuteChanged();
        AttemptedCommand.RaiseCanExecuteChanged();
        FollowUpCommand.RaiseCanExecuteChanged();
    }

    private static bool TryParseThreshold(string value, out decimal threshold) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out threshold) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out threshold);

    private static string FormatFleetPercent(decimal? value, int included, int total)
    {
        var percentage = value is null
            ? "N/A"
            : $"{value.Value.ToString("0.0", CultureInfo.CurrentCulture)}%";
        return $"{percentage} ({included.ToString(CultureInfo.CurrentCulture)}/{total.ToString(CultureInfo.CurrentCulture)})";
    }
}
