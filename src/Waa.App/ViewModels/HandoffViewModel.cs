using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;

namespace Waa.App.ViewModels;

public sealed class HandoffViewModel : ObservableObject
{
    private readonly WaaRepository _repository;
    private readonly HandoffService _handoffService;
    private readonly IClipboardService _clipboardService;
    private readonly Action<string> _reportStatus;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _timeZone;
    private string _draftText = string.Empty;
    private string _summaryText = "Handoff has not been generated.";
    private bool _isBusy;

    public HandoffViewModel(
        WaaRepository repository,
        HandoffService handoffService,
        IClipboardService clipboardService,
        Action<string> reportStatus,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null)
    {
        _repository = repository;
        _handoffService = handoffService;
        _clipboardService = clipboardService;
        _reportStatus = reportStatus;
        _now = now ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;

        RegenerateCommand = new AsyncRelayCommand(RegenerateAsync, () => !IsBusy);
        CopyCommand = new AsyncRelayCommand(CopyAsync, () => !IsBusy && DraftText.Length > 0);
    }

    public AsyncRelayCommand RegenerateCommand { get; }
    public AsyncRelayCommand CopyCommand { get; }

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
            }
        }
    }

    public Task OpenAsync() => RegenerateAsync();

    public async Task RegenerateAsync()
    {
        try
        {
            IsBusy = true;
            var day = LocalDayRange.Create(_now(), _timeZone);
            var entries = await Task.Run(() => _repository.LoadHandoffEntries(
                day.StartUtc,
                day.EndUtc));
            var result = _handoffService.Generate(entries, day);
            DraftText = result.Text;
            SummaryText =
                $"{result.NeedsFollowUpCount} follow-up  •  " +
                $"{result.WaitingCount} waiting  •  " +
                $"{result.CompletedTodayCount} completed today";
            _reportStatus($"Handoff regenerated from saved work for {day.LocalDate:M/d/yyyy}.");
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
}
