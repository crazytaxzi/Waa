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
    private readonly WorkspaceNavigator _navigator = new();
    private IReadOnlyList<FleetDriverRecord> _records = Array.Empty<FleetDriverRecord>();
    private MissingBolFleetState _missingBolFleetState = EmptyMissingBolFleetState();
    private decimal _threshold = 50m;
    private DriverRowViewModel? _selectedDriver;
    private WorkspaceViewModel _currentWorkspace = new FleetQueueWorkspaceViewModel();
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
    private bool _suppressSelectionLoad;

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
        NextWorkItemCommand = new AsyncRelayCommand(
            NextWorkItemAsync,
            CanMoveNext);
        OpenHandoffCommand = new AsyncRelayCommand(
            OpenHandoffAsync,
            () => !IsBusy && CurrentRoute != WorkspaceRoute.Handoff);
        BackToQueueCommand = new AsyncRelayCommand(
            BackToQueueAsync,
            () => !IsBusy && CurrentRoute != WorkspaceRoute.FleetQueue);
        BackCommand = new AsyncRelayCommand(
            NavigateBackAsync,
            () => !IsBusy && CanGoBack);
        OpenUnmatchedBolCommand = new AsyncRelayCommand(
            OpenUnmatchedBolAsync,
            () => !IsBusy && HasUnmatchedBol);
        ToggleUnmatchedBolCommand = new AsyncRelayCommand(
            ToggleUnmatchedBolAsync,
            () => !IsBusy && HasUnmatchedBol);
        OpenDriverCommand = new AsyncRelayCommand<DriverRowViewModel>(
            driver => driver is null
                ? Task.CompletedTask
                : NavigateToDriverAsync(driver.DriverCode),
            driver => !IsBusy && driver is not null);
        OpenDriverMissingBolCommand = new AsyncRelayCommand<DriverRowViewModel>(
            driver => driver is null
                ? Task.CompletedTask
                : NavigateToDriverAsync(driver.DriverCode, DriverWorkspaceFocus.MissingBol),
            driver => !IsBusy && driver is not null);
        OpenDriverWorkCommand = new AsyncRelayCommand<DriverRowViewModel>(
            driver => driver is null
                ? Task.CompletedTask
                : NavigateToDriverAsync(driver.DriverCode, DriverWorkspaceFocus.OpenWork),
            driver => !IsBusy && driver is not null);
        OpenAttentionItemCommand = new AsyncRelayCommand<DriverAttentionItemViewModel>(
            item => item is null ? Task.CompletedTask : OpenAttentionItemAsync(item, addToHistory: true),
            item => !IsBusy && item is not null);
        OpenNewWorkCommand = new AsyncRelayCommand(
            OpenNewWorkAsync,
            () => !IsBusy && SelectedDriver is not null);
        OpenActivityDetailCommand = new AsyncRelayCommand<WorkEntryItemViewModel>(
            item => item is null ? Task.CompletedTask : OpenActivityDetailAsync(item),
            item => !IsBusy && item is not null);
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
    public AsyncRelayCommand NextWorkItemCommand { get; }
    public AsyncRelayCommand OpenHandoffCommand { get; }
    public AsyncRelayCommand BackToQueueCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand OpenUnmatchedBolCommand { get; }
    public AsyncRelayCommand ToggleUnmatchedBolCommand { get; }
    public AsyncRelayCommand<DriverRowViewModel> OpenDriverCommand { get; }
    public AsyncRelayCommand<DriverRowViewModel> OpenDriverMissingBolCommand { get; }
    public AsyncRelayCommand<DriverRowViewModel> OpenDriverWorkCommand { get; }
    public AsyncRelayCommand<DriverAttentionItemViewModel> OpenAttentionItemCommand { get; }
    public AsyncRelayCommand OpenNewWorkCommand { get; }
    public AsyncRelayCommand<WorkEntryItemViewModel> OpenActivityDetailCommand { get; }

    public DriverRowViewModel? SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            var previousCode = _selectedDriver?.DriverCode;
            if (SetProperty(ref _selectedDriver, value))
            {
                RefreshCommandStates();
                if (!_suppressSelectionLoad &&
                    !string.Equals(previousCode, value?.DriverCode, StringComparison.OrdinalIgnoreCase))
                {
                    BeginSelectedDriverLoad(value);
                }
            }
        }
    }

    public WorkspaceViewModel CurrentWorkspace
    {
        get => _currentWorkspace;
        private set
        {
            if (SetProperty(ref _currentWorkspace, value))
            {
                OnPropertyChanged(nameof(CurrentRoute));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(BackLabel));
                OnPropertyChanged(nameof(BreadcrumbText));
                OnPropertyChanged(nameof(IsHandoffView));
                OnPropertyChanged(nameof(IsUnmatchedBolVisible));
                RefreshCommandStates();
            }
        }
    }

    public WorkspaceRoute CurrentRoute => CurrentWorkspace.Route;
    public bool CanGoBack => CurrentRoute != WorkspaceRoute.FleetQueue && _navigator.CanGoBack;
    public string BackLabel => CurrentWorkspace.BackLabel;
    public string BreadcrumbText => CurrentWorkspace.Breadcrumb;
    public bool IsHandoffView => CurrentRoute == WorkspaceRoute.Handoff;
    public bool IsUnmatchedBolVisible => CurrentRoute == WorkspaceRoute.UnmatchedBol;
    public bool HasVisibleDrivers => Drivers.Count > 0;
    public bool HasNoVisibleDrivers => !HasVisibleDrivers;

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

    public bool HasUnmatchedBol => UnmatchedBolItems.Count > 0;

    public string UnmatchedBolButtonText =>
        $"Unmatched BOL: {UnmatchedBolItems.Count.ToString(CultureInfo.CurrentCulture)}";

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
            _navigator.Reset();
            CurrentWorkspace = new FleetQueueWorkspaceViewModel();
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

    public async Task NavigateToDriverAsync(
        string driverCode,
        DriverWorkspaceFocus focus = DriverWorkspaceFocus.General)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverCode);
        await NavigateToLocationAsync(
            new WorkspaceLocation(WorkspaceRoute.DriverWorkspace, driverCode, null, focus),
            addToHistory: true);
    }

    public async Task NavigateBackAsync()
    {
        if (CurrentRoute is WorkspaceRoute.Handoff or WorkspaceRoute.UnmatchedBol)
        {
            await BackToQueueAsync();
            return;
        }

        var target = _navigator.Back();
        await ShowLocationAsync(target);
    }

    private async Task UpdateReportsAsync(bool isLaunchUpdate)
    {
        if (IsBusy)
        {
            return;
        }

        var preserveLocation = _navigator.Current;
        var preserveCode = preserveLocation.DriverCode ?? SelectedDriver?.DriverCode;
        try
        {
            IsBusy = true;
            StatusMessage = isLaunchUpdate
                ? "Checking Downloads once for the launch report update…"
                : "Updating reports from Downloads…";

            var result = await _updateService.UpdateAsync();
            await ReloadFleetAsync(preserveCode);
            await RestoreLocationAsync(preserveLocation);
            StatusMessage = result.Message;
            AppLog.Write(result.Message);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Report update failed");
            StatusMessage = $"Report update failed: {exception.Message}. The saved roster, Missing BOL state, work history, and unsaved notes were left unchanged.";
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
            await RestoreLocationAsync(_navigator.Current);
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
        var preserveLocation = _navigator.Current;
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
            await RestoreLocationAsync(preserveLocation);

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
        var moved = SelectNextNeedingAttention(SelectedDriver?.DriverCode, showEmptyStatus: true);
        if (moved && CurrentRoute != WorkspaceRoute.FleetQueue && SelectedDriver is not null)
        {
            _navigator.Reset();
            _navigator.Navigate(new WorkspaceLocation(
                WorkspaceRoute.DriverWorkspace,
                SelectedDriver.DriverCode));
            await ShowLocationAsync(_navigator.Current);
        }
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

    private async Task NextWorkItemAsync()
    {
        var driver = SelectedDriver;
        if (driver is null)
        {
            return;
        }

        var items = BuildAttentionItems(driver);
        var currentKey = CurrentWorkspace switch
        {
            IdleTaskWorkspaceViewModel => $"idle:{driver.DriverCode}",
            MissingBolTaskWorkspaceViewModel bol => $"bol:{bol.Item.Record.Id}",
            WorkItemTaskWorkspaceViewModel work => $"work:{work.Item.Record.Id}",
            _ => null
        };
        DriverAttentionItemViewModel? next;
        if (currentKey is null)
        {
            next = items.FirstOrDefault();
        }
        else
        {
            var index = items.ToList().FindIndex(item => item.Key == currentKey);
            next = index >= 0 && index + 1 < items.Count ? items[index + 1] : null;
        }

        if (next is not null)
        {
            await OpenAttentionItemAsync(
                next,
                addToHistory: CurrentRoute == WorkspaceRoute.DriverWorkspace);
            return;
        }

        var moved = SelectNextNeedingAttention(driver.DriverCode, showEmptyStatus: true);
        if (!moved || SelectedDriver is null)
        {
            return;
        }

        _navigator.Reset();
        _navigator.Navigate(new WorkspaceLocation(
            WorkspaceRoute.DriverWorkspace,
            SelectedDriver.DriverCode));
        await ShowLocationAsync(_navigator.Current);
    }

    private async Task OpenHandoffAsync()
    {
        await NavigateToLocationAsync(new WorkspaceLocation(WorkspaceRoute.Handoff), addToHistory: true);
        await Handoff.OpenAsync();
    }

    private async Task BackToQueueAsync()
    {
        _navigator.Reset();
        CurrentWorkspace = new FleetQueueWorkspaceViewModel();
        StatusMessage = "Returned to the fleet queue.";
        await Task.CompletedTask;
    }

    private Task OpenUnmatchedBolAsync() =>
        NavigateToLocationAsync(new WorkspaceLocation(WorkspaceRoute.UnmatchedBol), addToHistory: true);

    private async Task ToggleUnmatchedBolAsync()
    {
        if (CurrentRoute == WorkspaceRoute.UnmatchedBol)
        {
            await BackToQueueAsync();
        }
        else
        {
            await OpenUnmatchedBolAsync();
        }
    }

    private Task OpenNewWorkAsync()
    {
        var driver = SelectedDriver;
        return driver is null
            ? Task.CompletedTask
            : NavigateToLocationAsync(
                new WorkspaceLocation(WorkspaceRoute.NewWork, driver.DriverCode),
                addToHistory: true);
    }

    private Task OpenActivityDetailAsync(WorkEntryItemViewModel item)
    {
        var driver = SelectedDriver;
        return driver is null
            ? Task.CompletedTask
            : NavigateToLocationAsync(
                new WorkspaceLocation(
                    WorkspaceRoute.ActivityDetail,
                    driver.DriverCode,
                    item.Record.Id),
                addToHistory: true);
    }

    private async Task OpenAttentionItemAsync(
        DriverAttentionItemViewModel item,
        bool addToHistory)
    {
        var driver = SelectedDriver;
        if (driver is null)
        {
            return;
        }

        var location = item.Kind switch
        {
            DriverAttentionKind.Idle => new WorkspaceLocation(
                WorkspaceRoute.IdleTask,
                driver.DriverCode),
            DriverAttentionKind.MissingBol when item.MissingBolItem is not null => new WorkspaceLocation(
                WorkspaceRoute.MissingBolTask,
                driver.DriverCode,
                item.MissingBolItem.Record.Id),
            DriverAttentionKind.ManualWork when item.WorkItem is not null => new WorkspaceLocation(
                WorkspaceRoute.WorkItemTask,
                driver.DriverCode,
                item.WorkItem.Record.Id),
            _ => null
        };

        if (location is not null)
        {
            await NavigateToLocationAsync(location, addToHistory);
        }
    }

    private async Task OnWorkChangedAsync(string driverCode)
    {
        var preserveLocation = _navigator.Current;
        await ReloadFleetAsync(driverCode);
        if (preserveLocation.Route == WorkspaceRoute.NewWork)
        {
            var parent = _navigator.Back();
            if (parent.Route != WorkspaceRoute.DriverWorkspace ||
                !string.Equals(parent.DriverCode, driverCode, StringComparison.OrdinalIgnoreCase))
            {
                _navigator.Replace(new WorkspaceLocation(
                    WorkspaceRoute.DriverWorkspace,
                    driverCode));
            }

            await ShowLocationAsync(_navigator.Current, Work.LastSavedWorkEntryId);
            return;
        }

        await RestoreLocationAsync(preserveLocation);
    }

    private async Task OnMissingBolChangedAsync(string driverCode)
    {
        var preserveLocation = _navigator.Current;
        await ReloadFleetAsync(driverCode);
        await RestoreLocationAsync(preserveLocation);
    }

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
        await LoadSelectedDriverStateAsync(SelectedDriver);
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

        _suppressSelectionLoad = true;
        try
        {
            SelectedDriver = previousCode is null
                ? Drivers.FirstOrDefault()
                : Drivers.FirstOrDefault(driver => driver.DriverCode.Equals(previousCode, StringComparison.OrdinalIgnoreCase))
                  ?? Drivers.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionLoad = false;
        }

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
        OnPropertyChanged(nameof(HasVisibleDrivers));
        OnPropertyChanged(nameof(HasNoVisibleDrivers));
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

    private async Task NavigateToLocationAsync(
        WorkspaceLocation location,
        bool addToHistory)
    {
        if (location.DriverCode is not null)
        {
            var driver = FindDriver(location.DriverCode);
            if (driver is not null)
            {
                _suppressSelectionLoad = true;
                try
                {
                    SelectedDriver = driver;
                }
                finally
                {
                    _suppressSelectionLoad = false;
                }

                await LoadSelectedDriverStateAsync(driver);
            }
        }

        _navigator.Navigate(location, addToHistory);
        await ShowLocationAsync(location);
    }

    private async Task RestoreLocationAsync(WorkspaceLocation location)
    {
        _navigator.Replace(location);
        if (location.DriverCode is not null)
        {
            var driver = FindDriver(location.DriverCode);
            if (driver is not null)
            {
                _suppressSelectionLoad = true;
                try
                {
                    SelectedDriver = driver;
                }
                finally
                {
                    _suppressSelectionLoad = false;
                }

                await LoadSelectedDriverStateAsync(driver);
            }
        }

        await ShowLocationAsync(location);
    }

    private async Task ShowLocationAsync(
        WorkspaceLocation location,
        long? highlightedWorkEntryId = null)
    {
        CurrentWorkspace = await BuildWorkspaceAsync(location, highlightedWorkEntryId);
    }

    private async Task<WorkspaceViewModel> BuildWorkspaceAsync(
        WorkspaceLocation location,
        long? highlightedWorkEntryId)
    {
        if (location.Route == WorkspaceRoute.FleetQueue)
        {
            return new FleetQueueWorkspaceViewModel();
        }

        if (location.Route == WorkspaceRoute.Handoff)
        {
            return new HandoffWorkspaceViewModel();
        }

        if (location.Route == WorkspaceRoute.UnmatchedBol)
        {
            return new UnmatchedBolWorkspaceViewModel(UnmatchedBolItems.ToArray());
        }

        var driver = location.DriverCode is null ? null : FindDriver(location.DriverCode);
        if (driver is null)
        {
            return new UnavailableWorkspaceViewModel(
                "Workspace unavailable",
                "Fleet > Unavailable",
                "This driver is no longer available in the current fleet. Return to the fleet queue and choose another driver.",
                "Back to Fleet");
        }

        return location.Route switch
        {
            WorkspaceRoute.DriverWorkspace => new DriverWorkspaceViewModel(
                driver,
                BuildAttentionItems(driver),
                Work.TodayEntries.ToArray(),
                location.Focus,
                highlightedWorkEntryId),
            WorkspaceRoute.IdleTask => new IdleTaskWorkspaceViewModel(driver),
            WorkspaceRoute.MissingBolTask => await BuildMissingBolTaskWorkspaceAsync(driver, location.ItemId),
            WorkspaceRoute.WorkItemTask => BuildWorkItemTaskWorkspace(driver, location.ItemId),
            WorkspaceRoute.NewWork => new NewWorkWorkspaceViewModel(driver),
            WorkspaceRoute.ActivityDetail => BuildActivityDetailWorkspace(driver, location.ItemId),
            _ => new UnavailableWorkspaceViewModel(
                "Workspace unavailable",
                $"Fleet > {driver.DriverName} > Unavailable",
                "This work item is no longer available. Return to the driver or fleet queue.",
                "Back to Driver")
        };
    }

    private IReadOnlyList<DriverAttentionItemViewModel> BuildAttentionItems(DriverRowViewModel driver)
    {
        var items = new List<DriverAttentionItemViewModel>();
        var linkedIdle = Work.OpenEntries.FirstOrDefault(item =>
            item.Record.Source == WorkEntrySource.IdleContact);
        if (driver.NeedsIdleAttention || linkedIdle is not null)
        {
            items.Add(new DriverAttentionItemViewModel(
                DriverAttentionKind.Idle,
                $"idle:{driver.DriverCode}",
                "IDLE",
                $"{driver.Idle28Display} 28D / {driver.Idle7Display} 7D",
                driver.ContactDisplay,
                $"Threshold {driver.Threshold.ToString("0.0", CultureInfo.CurrentCulture)}%  •  Unit {driver.UnitCode}  •  Leader {driver.DriverLeader}",
                driver.Record.ReportCycleDate.ToString("M/d/yyyy", CultureInfo.CurrentCulture),
                driver.Record.LatestOutcome == IdleContactOutcome.SpokeFollowUp
                    ? SemanticState.FollowUp
                    : SemanticState.Warning,
                linkedIdle));
        }

        if (MissingBol is not null)
        {
            foreach (var bol in MissingBol.Items.Where(item => !item.IsResolved))
            {
                items.Add(new DriverAttentionItemViewModel(
                    DriverAttentionKind.MissingBol,
                    $"bol:{bol.Record.Id}",
                    "MISSING BOL",
                    $"Order {bol.OrderNumber}",
                    bol.StatusDisplay,
                    $"Empty call {bol.EmptyCallDateDisplay}  •  {bol.RouteDisplay}",
                    bol.EmptyCallDateDisplay,
                    bol.SemanticState,
                    missingBolItem: bol));
            }
        }

        foreach (var work in Work.OpenEntries
                     .Where(item => item.Record.Source == WorkEntrySource.Manual)
                     .OrderBy(item => item.Record.Status == WorkEntryStatus.FollowUp ? 0 : 1)
                     .ThenBy(item => item.Record.CreatedUtc)
                     .ThenBy(item => item.Record.Id))
        {
            items.Add(new DriverAttentionItemViewModel(
                DriverAttentionKind.ManualWork,
                $"work:{work.Record.Id}",
                work.StatusDisplay.ToUpperInvariant(),
                Truncate(work.Text, 90),
                work.StatusDisplay,
                $"Open since {work.CreatedDisplay}  •  Unit {work.UnitCodeDisplay}",
                work.CreatedDisplay,
                work.SemanticState,
                work));
        }

        return items;
    }

    private async Task<WorkspaceViewModel> BuildMissingBolTaskWorkspaceAsync(
        DriverRowViewModel driver,
        long? itemId)
    {
        if (itemId is null || MissingBol is null || _missingBolRepository is null)
        {
            return MissingTaskUnavailable(driver);
        }

        var item = MissingBol.FindItem(itemId.Value);
        if (item is null)
        {
            return MissingTaskUnavailable(driver);
        }

        var history = await Task.Run(() => _missingBolRepository.LoadActionHistory(itemId.Value));
        return new MissingBolTaskWorkspaceViewModel(
            driver,
            item,
            history.Select(record => new MissingBolActionHistoryItemViewModel(record)).ToArray());
    }

    private WorkspaceViewModel BuildWorkItemTaskWorkspace(
        DriverRowViewModel driver,
        long? itemId)
    {
        var item = FindLoadedWorkItem(itemId);
        return item is null
            ? WorkTaskUnavailable(driver)
            : new WorkItemTaskWorkspaceViewModel(driver, item);
    }

    private WorkspaceViewModel BuildActivityDetailWorkspace(
        DriverRowViewModel driver,
        long? itemId)
    {
        var item = Work.TodayEntries.FirstOrDefault(entry => entry.Record.Id == itemId);
        return item is null
            ? new UnavailableWorkspaceViewModel(
                "Activity unavailable",
                $"Fleet > {driver.DriverName} > Activity unavailable",
                "This activity is no longer in the current local-day view. Return to the driver workspace.",
                "Back to Driver")
            : new ActivityDetailWorkspaceViewModel(driver, item);
    }

    private WorkEntryItemViewModel? FindLoadedWorkItem(long? itemId)
    {
        if (itemId is null)
        {
            return null;
        }

        return Work.OpenEntries.Concat(Work.TodayEntries)
            .FirstOrDefault(item => item.Record.Id == itemId.Value);
    }

    private DriverRowViewModel? FindDriver(string driverCode) =>
        Drivers.FirstOrDefault(driver =>
            driver.DriverCode.Equals(driverCode, StringComparison.OrdinalIgnoreCase))
        ?? _records
            .Where(record => record.DriverCode.Equals(driverCode, StringComparison.OrdinalIgnoreCase))
            .Select(CreateDriverRow)
            .FirstOrDefault();

    private async Task LoadSelectedDriverStateAsync(DriverRowViewModel? selectedDriver)
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

    private bool CanRecordContact() =>
        !IsBusy &&
        !Work.IsBusy &&
        MissingBol?.IsBusy != true &&
        CurrentRoute != WorkspaceRoute.Handoff &&
        SelectedDriver is not null;

    private bool CanMoveNext() =>
        !IsBusy &&
        !Work.IsBusy &&
        MissingBol?.IsBusy != true &&
        CurrentRoute != WorkspaceRoute.Handoff &&
        SelectedDriver is not null;

    private void RefreshCommandStates()
    {
        UpdateReportsCommand.RaiseCanExecuteChanged();
        ApplyThresholdCommand.RaiseCanExecuteChanged();
        SpokeCommand.RaiseCanExecuteChanged();
        AttemptedCommand.RaiseCanExecuteChanged();
        FollowUpCommand.RaiseCanExecuteChanged();
        NextNeedingAttentionCommand.RaiseCanExecuteChanged();
        NextWorkItemCommand.RaiseCanExecuteChanged();
        OpenHandoffCommand.RaiseCanExecuteChanged();
        BackToQueueCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        OpenUnmatchedBolCommand.RaiseCanExecuteChanged();
        ToggleUnmatchedBolCommand.RaiseCanExecuteChanged();
        OpenDriverCommand.RaiseCanExecuteChanged();
        OpenDriverMissingBolCommand.RaiseCanExecuteChanged();
        OpenDriverWorkCommand.RaiseCanExecuteChanged();
        OpenAttentionItemCommand.RaiseCanExecuteChanged();
        OpenNewWorkCommand.RaiseCanExecuteChanged();
        OpenActivityDetailCommand.RaiseCanExecuteChanged();
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
            await LoadSelectedDriverStateAsync(selectedDriver);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Selected driver state load failed");
            StatusMessage = $"Selected driver work could not be loaded: {exception.Message}";
        }
    }

    private static WorkspaceViewModel MissingTaskUnavailable(DriverRowViewModel driver) =>
        new UnavailableWorkspaceViewModel(
            "Missing BOL unavailable",
            $"Fleet > {driver.DriverName} > Missing BOL unavailable",
            "This Missing BOL item is no longer available for this driver. Return to the driver workspace.",
            "Back to Driver");

    private static WorkspaceViewModel WorkTaskUnavailable(DriverRowViewModel driver) =>
        new UnavailableWorkspaceViewModel(
            "Work item unavailable",
            $"Fleet > {driver.DriverName} > Work item unavailable",
            "This work item is no longer available in the current driver view. Return to the driver workspace.",
            "Back to Driver");

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

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : $"{value[..(length - 1)]}…";
}
