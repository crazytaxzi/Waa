using System.Collections.ObjectModel;
using System.Globalization;
using Waa.App.Data;
using Waa.App.Infrastructure;

namespace Waa.App.ViewModels;

public sealed class MissingBolViewModel : ObservableObject
{
    private readonly MissingBolRepository _repository;
    private readonly Action<string> _reportStatus;
    private FleetDriverRecord? _driver;
    private string _summaryText = "Select a driver to review the current Missing BOL report.";
    private bool _isBusy;
    private int _loadVersion;

    public MissingBolViewModel(
        MissingBolRepository repository,
        Func<string, Task> onStateChanged,
        Action<string> reportStatus)
    {
        _repository = repository;
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

    public MissingBolItemViewModel? FindItem(long itemId) =>
        Items.FirstOrDefault(item => item.Record.Id == itemId);

    public async Task SetDriverAsync(FleetDriverRecord? driver)
    {
        _driver = driver;
        OnPropertyChanged(nameof(HasDriver));
        var version = ++_loadVersion;
        if (driver is null)
        {
            Items.Clear();
            OnPropertyChanged(nameof(HasItems));
            SummaryText = "Select a driver to review the current Missing BOL report.";
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
                Items.Add(new MissingBolItemViewModel(record));
            }

            OnPropertyChanged(nameof(HasItems));
            SummaryText = records.Count == 0
                ? "No Missing BOL orders for this driver in the current workbook."
                : $"{records.Count.ToString(CultureInfo.CurrentCulture)} order{(records.Count == 1 ? string.Empty : "s")} in the current workbook — read-only.";
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Selected driver Missing BOL load failed");
            _reportStatus($"Missing BOL report rows could not be loaded: {exception.Message}");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }
}

public sealed class MissingBolItemViewModel
{
    public MissingBolItemViewModel(MissingBolItemRecord record)
    {
        Record = record;
    }

    public MissingBolItemRecord Record { get; }
    public string OrderNumber => Record.SourceOrderNumber;
    public string EmptyCallDateDisplay => Record.EmptyCallDate.ToString("M/d/yyyy", CultureInfo.CurrentCulture);
    public string StatusDisplay => "In current report";

    // The old Driver Workspace builder only adds BOL rows to NEEDS ATTENTION when
    // IsResolved is false. Source-only BOL rows are information, not actionable
    // work, so keep them out of that list (and therefore out of Next Work Item).
    // They are displayed in their own CURRENT MISSING BOL section instead.
    public bool IsResolved => true;

    public DriverAttentionItemViewModel AttentionItem => new(
        DriverAttentionKind.MissingBol,
        $"bol:{Record.Id}",
        "MISSING BOL",
        $"Order {OrderNumber}",
        StatusDisplay,
        $"Empty call {EmptyCallDateDisplay}  •  {RouteDisplay}",
        EmptyCallDateDisplay,
        SemanticState,
        missingBolItem: this);

    public string RouteDisplay => FormatRoute(Record.OriginCityState, Record.DestinationCityState);
    public string CustomerDisplay => Record.BillTo.Length == 0
        ? "Customer not supplied"
        : Record.BillTo;
    public string MilesDisplay => FormatMiles(Record.LoadedMiles, Record.OrderLevelMiles);
    public string SourceEvidence => FormatSourceEvidence(Record.SourceDriverCode, Record.SourceDriverName);
    public string SourceDriverCodeDisplay => Record.SourceDriverCode.Length == 0
        ? "(blank)"
        : Record.SourceDriverCode;
    public string SourceDriverNameDisplay => Record.SourceDriverName.Length == 0
        ? "Not supplied"
        : Record.SourceDriverName;
    public string PresenceDisplay => "Current workbook";
    public string NameWarning => Record.SourceNameDiffersFromDriver
        ? $"Exact Driver Code matched; source name “{Record.SourceDriverName}” differs from WAA name “{Record.MatchedDriverName}”."
        : string.Empty;
    public bool HasNameWarning => NameWarning.Length > 0;
    public SemanticState SemanticState => SemanticState.Information;

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
            return "Miles not supplied";
        }

        if (loadedMiles is not null && orderMiles is not null)
        {
            return $"{loadedMiles.Value:0.##} loaded / {orderMiles.Value:0.##} order";
        }

        return loadedMiles is not null
            ? $"{loadedMiles.Value:0.##} loaded"
            : $"{orderMiles!.Value:0.##} order";
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
    public string SourceDriverName => Record.SourceDriverName.Length == 0
        ? "Not supplied"
        : Record.SourceDriverName;
    public string RouteDisplay => Record.OriginCityState.Length > 0 && Record.DestinationCityState.Length > 0
        ? $"{Record.OriginCityState} → {Record.DestinationCityState}"
        : Record.OriginCityState.Length > 0
            ? $"Origin: {Record.OriginCityState}"
            : Record.DestinationCityState.Length > 0
                ? $"Destination: {Record.DestinationCityState}"
                : "Route not supplied";
    public string PresenceDisplay => "Current workbook";
    public string ExactMatchExplanation =>
        "No exact current durable Driver Code exists in WAA for this workbook row. It is shown read-only; name, unit, leader, and fuzzy matching are not used.";
}