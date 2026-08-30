using System.Globalization;
using Waa.App.Data;

namespace Waa.App.ViewModels;

public sealed class WorkEntryItemViewModel
{
    private readonly TimeZoneInfo _timeZone;

    public WorkEntryItemViewModel(
        WorkEntryRecord record,
        Func<long, Task> resolve,
        Func<long, Task> reopen,
        TimeZoneInfo timeZone)
    {
        Record = record;
        _timeZone = timeZone;
        ResolveCommand = new AsyncRelayCommand(
            () => resolve(record.Id),
            () => CanResolve);
        ReopenCommand = new AsyncRelayCommand(
            () => reopen(record.Id),
            () => CanReopen);
    }

    public WorkEntryRecord Record { get; }
    public AsyncRelayCommand ResolveCommand { get; }
    public AsyncRelayCommand ReopenCommand { get; }
    public string Text => Record.Text;
    public bool IsResolved => Record.ResolvedUtc is not null;
    public bool UsesMissingBolControls => Record.Source == WorkEntrySource.MissingBolTask;
    public bool CanResolve =>
        !UsesMissingBolControls &&
        !IsResolved &&
        Record.Status is WorkEntryStatus.Waiting or WorkEntryStatus.FollowUp;
    public bool CanReopen =>
        !UsesMissingBolControls &&
        IsResolved &&
        Record.Status is WorkEntryStatus.Waiting or WorkEntryStatus.FollowUp;
    public string ResolutionInstruction => UsesMissingBolControls
        ? "Use the Missing BOL actions above to resolve or reopen this linked task."
        : string.Empty;
    public string StatusDisplay => Record.Status switch
    {
        WorkEntryStatus.FollowUp => "Follow-up",
        _ => Record.Status.ToString()
    };
    public string SourceDisplay => Record.Source switch
    {
        WorkEntrySource.IdleContact => "Idle contact",
        WorkEntrySource.MissingBolTask => "Missing BOL task",
        WorkEntrySource.MissingBolAction => "Missing BOL action",
        _ => "Manual"
    };
    public string CreatedDisplay => FormatLocal(Record.CreatedUtc);
    public string ResolutionDisplay => Record.ResolvedUtc is { } resolvedUtc
        ? $"Resolved {FormatLocal(resolvedUtc)}"
        : string.Empty;

    private string FormatLocal(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _timeZone)
            .ToString("M/d h:mm tt", CultureInfo.CurrentCulture);
}
