using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;

namespace Waa.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly WaaRepository _repository;
    private readonly MissingBolRepository? _missingBolRepository;
    private readonly ReportUpdateService _updateService;
    private IReadOnlyList<FleetDriverRecord> _records = Array.Empty<FleetDriverRecord>();
    private MissingBolFleetState _missingBolFleetState = EmptyMissingBolFleetState();
    private decimal _threshold = 50m;
    private DriverRowViewModel? _selectedDriver;
    private string _searchText = string.Empty;
    private string _thresholdText = "50.0";
    private string _contactNote = string.Empty;
    private string _reportCycleText = "No report";
    private string _fleet7DayText = "N/A";
    private string _fleet28DayText = "N/A";
    private string _contactProgressText = "0 need contact";
    private string _missingBolSummaryText = "Missing BOL: 0 open  •  0 unmatched";
    private string _statusMessage = "Starting WAA…";
    private string _rosterSummaryText = "No roster loaded";
    private bool _isBusy;
    private bool _initialized;
    private bool _isHandoffView;
    private bool _isUnmatchedBolVisible;

    public MainViewModel(
        WaaRepository repository,
        ReportUpdateService updateService,
        IClipboardService clipboardService,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null,
        MissingBolRepository? missingBolRepository = null)
    {
        _repository = repository;
        _missingBolRepository = missingBolRepository;
        _updateService = updateService;

        Work = new DriverWorkViewModel(
            repository,
            OnWorkChangedAsync,
            message => StatusMessage = message,
            now,
            timeZone,
            missingBolRepository);
        Work.PropertyChanged += OnWorkPropertyChanged;

        if (missingBolRepository is not null)
        {
            MissingBol = new MissingBolViewModel(
                missingBolRepository,
                OnMissingBolChangedAsync,
                message => StatusMessage = message);
            MissingBol.PropertyChanged += OnMissingBolPropertyChanged;
        }

        Handoff = new HandoffViewModel(
            repository,
            new HandoffService(),
            clipboardService,
            message => StatusMessage = message,
            now,
            timeZone,
            missingBolRepository);

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
        NextNeedingAttentionCommand = new AsyncRelayCommand(
            NextNeedingAttentionAsync,
            CanMoveNext);
        OpenHandoffCommand = new AsyncRelayCommand(
            OpenHandoffAsync,
            () => !IsBusy && !IsHandoffView);
        BackToQueueCommand = new AsyncRelayCommand(
            BackToQueueAsync,
            () => !IsBusy && IsHandoffView);
        ToggleUnmatchedBolCommand = new AsyncRelayCommand(
            ToggleUnmatchedBolAsync,
            () => !IsBusy && !IsHandoffView && HasUnmatchedBol);
    }

    public ObservableCollection<DriverRowViewModel> Drivers { get; } = new();
    public ObservableCollection<MissingBolUnmatchedItemViewModel> UnmatchedBolItems { get; } = new();
    public DriverWorkViewModel Work { get; }
    public MissingBolViewModel? MissingBol { get; }
    public HandoffViewModel Handoff { get; }
    public AsyncRelayCommand UpdateReportsCommand { get; }
    public AsyncRelayCommand ApplyThresholdCommand { get; }
    public AsyncRelayCommand SpokeCommand { get; }
    public AsyncRelayCommand AttemptedCommand { get; }
    public AsyncRelayCommand FollowUpCommand { get; }
    public AsyncRelayCommand NextNeedingAttentionCommand { get; }
    public AsyncRelayCommand OpenHandoffCommand { get; }
    public AsyncRelayCommand BackToQueueCommand { get; }
    public AsyncRelayCommand ToggleUnmatchedBolCommand { get; }

    public DriverRowViewModel? SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            if (SetProperty(ref _selectedDriver, value))
            {
                RefreshCommandStates();
                BeginSelectedDriverLoad(value);
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

    public string MissingBolSummaryText
    {
        get => _missingBolSummaryText;
        private set => SetProperty(ref _missingBolSummaryText, value);
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

    public bool IsHandoffView
    {
        get => _isHandoffView;
        private set
        {
            if (SetProperty(ref _isHandoffView, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public bool HasUnmatchedBol => UnmatchedBolItems.Count > 0;

    public string UnmatchedBolButtonText =>
        $"Unmatched BOL: {UnmatchedBolItems.Count.ToString(CultureInfo.CurrentCulture)}";

    public bool IsUnmatchedBolVisible
    {
        get => _isUnmatchedBolVisible;
        private set => SetProperty(ref _isUnmatchedBolVisible, value);
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
            StatusMessage = "Loading saved roster and work history…";
            await Task.Run(() =>
            {
                _repository.Initialize();
                _missingBolRepository?.Initialize();
            });
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
            StatusMessage = $"Report update failed: {exception.Message}. The saved roster, Missing BOL state, and work history were left unchanged.";
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
            var advanced = SelectNextNeedingAttention(currentCode, showEmptyStatus: false);
            if (!advanced)
            {
                await Work.RefreshAsync();
            }

            var outcomeText = outcome switch
            {
                IdleContactOutcome.Spoke => "Spoke",
                IdleContactOutcome.Attempted => "Attempted",
                IdleContactOutcome.SpokeFollowUp => "Spoke — Follow-up",
                _ => outcome.ToString()
            };
            StatusMessage = $"{outcomeText} and linked work saved for {selected.DriverName} for cycle {selected.Record.ReportCycleDate:M/d/yyyy}.";
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Idle contact save failed");
            StatusMessage = $"Idle contact and work were not saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NextNeedingAttentionAsync()
    {
        await Task.Yield();
        _ = SelectNextNeedingAttention(SelectedDriver?.DriverCode, showEmptyStatus: true);
    }

    private bool SelectNextNeedingAttention(string? currentCode, bool showEmptyStatus)
    {
        if (Drivers.Count == 0)
        {
            if (showEmptyStatus)
            {
                StatusMessage = "No visible drivers currently need attention.";
            }

            return false;
        }

        var visible = Drivers.ToArray();
        var currentIndex = currentCode is null
            ? -1
            : Array.FindIndex(
                visible,
                driver => driver.DriverCode.Equals(currentCode, StringComparison.OrdinalIgnoreCase));
        var rotated = currentIndex < 0
            ? visible.AsEnumerable()
            : visible
                .Skip(currentIndex + 1)
                .Concat(visible.Take(currentIndex + 1));
        var otherDrivers = rotated
            .Where(driver => currentCode is null ||
                !driver.DriverCode.Equals(currentCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var next = otherDrivers.FirstOrDefault(driver => driver.NeedsIdleAttention)
            ?? otherDrivers.FirstOrDefault(driver => driver.HasOpenWork);

        if (next is null)
        {
            if (showEmptyStatus)
            {
                StatusMessage = "No other visible drivers currently need attention.";
            }

            return false;
        }

        SelectedDriver = next;
        StatusMessage = $"Selected {next.DriverName} — {next.NeedsIdleAttention switch { true => "idle contact needs attention", false => next.OpenWorkDisplay }}.";
        return true;
    }

    private async Task OpenHandoffAsync()
    {
        IsUnmatchedBolVisible = false;
        IsHandoffView = true;
        await Handoff.OpenAsync();
    }

    private Task BackToQueueAsync()
    {
        IsHandoffView = false;
        StatusMessage = "Returned to the driver work queue.";
        return Task.CompletedTask;
    }

    private Task ToggleUnmatchedBolAsync()
    {
        IsUnmatchedBolVisible = !IsUnmatchedBolVisible;
        StatusMessage = IsUnmatchedBolVisible
            ? "Showing unmatched Missing BOL items. They remain read-only until an exact Driver Code exists in WAA."
            : "Unmatched Missing BOL list hidden.";
        return Task.CompletedTask;
    }

    private async Task OnWorkChangedAsync(string driverCode) =>
        await ReloadFleetAsync(driverCode);

    private async Task OnMissingBolChangedAsync(string driverCode) =>
        await ReloadFleetAsync(driverCode);

    private async Task ReloadFleetAsync(string? preferredDriverCode = null)
    {
        var loaded = await Task.Run(() =>
        {
            var fleet = _repository.LoadFleet();
            var missingBol = _missingBolRepository?.LoadFleetState() ?? EmptyMissingBolFleetState();
            return (Fleet: fleet, MissingBol: missingBol);
        });
        var state = loaded.Fleet;
        _missingBolFleetState = loaded.MissingBol;
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
        MissingBolSummaryText =
            $"Missing BOL: {_missingBolFleetState.OpenMatchedCount.ToString(CultureInfo.CurrentCulture)} open  •  " +
            $"{_missingBolFleetState.UnmatchedItems.Count.ToString(CultureInfo.CurrentCulture)} unmatched";

        UnmatchedBolItems.Clear();
        foreach (var item in _missingBolFleetState.UnmatchedItems)
        {
            UnmatchedBolItems.Add(new MissingBolUnmatchedItemViewModel(item));
        }

        OnPropertyChanged(nameof(HasUnmatchedBol));
        OnPropertyChanged(nameof(UnmatchedBolButtonText));
        if (!HasUnmatchedBol)
        {
            IsUnmatchedBolVisible = false;
        }

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
        var ordered = DriverQueueOrderer.Order(
            _records
                .Select(CreateDriverRow)
                .Where(MatchesSearch));

        Drivers.Clear();
        foreach (var driver in ordered)
        {
            Drivers.Add(driver);
        }

        SelectedDriver = previousCode is null
            ? Drivers.FirstOrDefault()
            : Drivers.FirstOrDefault(driver => driver.DriverCode.Equals(previousCode, StringComparison.OrdinalIgnoreCase))
              ?? Drivers.FirstOrDefault();

        var allRows = _records.Select(CreateDriverRow).ToArray();
        var needContact = allRows.Count(driver => driver.NeedsIdleAttention);
        var aboveThreshold = allRows.Count(driver => driver.IsAboveThreshold);
        var spoken = allRows.Count(driver =>
            driver.IsAboveThreshold &&
            driver.Record.LatestOutcome is IdleContactOutcome.Spoke or IdleContactOutcome.SpokeFollowUp);
        var openWorkDrivers = allRows.Count(driver => driver.HasOpenWork);

        ContactProgressText =
            $"{needContact.ToString(CultureInfo.CurrentCulture)} need  •  " +
            $"{spoken.ToString(CultureInfo.CurrentCulture)}/{aboveThreshold.ToString(CultureInfo.CurrentCulture)} spoken  •  " +
            $"{openWorkDrivers.ToString(CultureInfo.CurrentCulture)} with open work";
        RefreshCommandStates();
    }

    private DriverRowViewModel CreateDriverRow(FleetDriverRecord record)
    {
        _missingBolFleetState.DriverSummaries.TryGetValue(record.DriverCode, out var summary);
        return new DriverRowViewModel(record, _threshold, summary);
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
               driver.DriverLeader.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               driver.OrderSearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool CanRecordContact() =>
        !IsBusy &&
        !Work.IsBusy &&
        MissingBol?.IsBusy != true &&
        !IsHandoffView &&
        SelectedDriver is not null;

    private bool CanMoveNext() =>
        !IsBusy &&
        !Work.IsBusy &&
        MissingBol?.IsBusy != true &&
        !IsHandoffView &&
        SelectedDriver is not null;

    private void RefreshCommandStates()
    {
        UpdateReportsCommand.RaiseCanExecuteChanged();
        ApplyThresholdCommand.RaiseCanExecuteChanged();
        SpokeCommand.RaiseCanExecuteChanged();
        AttemptedCommand.RaiseCanExecuteChanged();
        FollowUpCommand.RaiseCanExecuteChanged();
        NextNeedingAttentionCommand.RaiseCanExecuteChanged();
        OpenHandoffCommand.RaiseCanExecuteChanged();
        BackToQueueCommand.RaiseCanExecuteChanged();
        ToggleUnmatchedBolCommand.RaiseCanExecuteChanged();
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriverWorkViewModel.IsBusy))
        {
            RefreshCommandStates();
        }
    }

    private void OnMissingBolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MissingBolViewModel.IsBusy))
        {
            RefreshCommandStates();
        }
    }

    private async void BeginSelectedDriverLoad(DriverRowViewModel? selectedDriver)
    {
        try
        {
            var workTask = Work.SetDriverAsync(selectedDriver?.Record);
            if (MissingBol is null)
            {
                await workTask;
                return;
            }

            await Task.WhenAll(
                workTask,
                MissingBol.SetDriverAsync(selectedDriver?.Record));
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Selected driver state load failed");
            StatusMessage = $"Selected driver work could not be loaded: {exception.Message}";
        }
    }

    private static MissingBolFleetState EmptyMissingBolFleetState() =>
        new(
            new Dictionary<string, MissingBolDriverSummary>(StringComparer.OrdinalIgnoreCase),
            0,
            Array.Empty<MissingBolUnmatchedRecord>());

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
