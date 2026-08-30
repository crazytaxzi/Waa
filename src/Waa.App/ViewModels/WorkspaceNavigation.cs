using System.Globalization;
using Waa.App.Data;

namespace Waa.App.ViewModels;

public enum WorkspaceRoute
{
    FleetQueue,
    DriverWorkspace,
    IdleTask,
    MissingBolTask,
    WorkItemTask,
    NewWork,
    ActivityDetail,
    Handoff,
    UnmatchedBol,
    Unavailable
}

public enum DriverWorkspaceFocus
{
    General,
    MissingBol,
    OpenWork
}

public enum DriverAttentionKind
{
    Idle,
    MissingBol,
    ManualWork
}

public enum SemanticState
{
    Warning,
    FollowUp,
    Completed,
    Quiet,
    Error,
    Information
}

public sealed record WorkspaceLocation(
    WorkspaceRoute Route,
    string? DriverCode = null,
    long? ItemId = null,
    DriverWorkspaceFocus Focus = DriverWorkspaceFocus.General)
{
    public static WorkspaceLocation FleetQueue { get; } = new(WorkspaceRoute.FleetQueue);
}

public sealed class WorkspaceNavigator
{
    private readonly Stack<WorkspaceLocation> _backStack = new();

    public WorkspaceLocation Current { get; private set; } = WorkspaceLocation.FleetQueue;
    public bool CanGoBack => _backStack.Count > 0;

    public void Navigate(WorkspaceLocation target, bool addCurrentToHistory = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target == Current)
        {
            return;
        }

        if (addCurrentToHistory)
        {
            _backStack.Push(Current);
        }

        Current = target;
    }

    public WorkspaceLocation Back()
    {
        if (_backStack.Count == 0)
        {
            Current = WorkspaceLocation.FleetQueue;
            return Current;
        }

        Current = _backStack.Pop();
        return Current;
    }

    public void Replace(WorkspaceLocation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Current = target;
    }

    public void Reset()
    {
        _backStack.Clear();
        Current = WorkspaceLocation.FleetQueue;
    }
}

public abstract class WorkspaceViewModel
{
    protected WorkspaceViewModel(
        WorkspaceRoute route,
        string title,
        string breadcrumb,
        string backLabel)
    {
        Route = route;
        Title = title;
        Breadcrumb = breadcrumb;
        BackLabel = backLabel;
    }

    public WorkspaceRoute Route { get; }
    public string Title { get; }
    public string Breadcrumb { get; }
    public string BackLabel { get; }
}

public sealed class FleetQueueWorkspaceViewModel : WorkspaceViewModel
{
    public FleetQueueWorkspaceViewModel()
        : base(WorkspaceRoute.FleetQueue, "Fleet Queue", "Fleet", string.Empty)
    {
    }
}

public sealed class DriverAttentionItemViewModel
{
    public DriverAttentionItemViewModel(
        DriverAttentionKind kind,
        string key,
        string typeLabel,
        string title,
        string statusText,
        string contextText,
        string dateText,
        SemanticState semanticState,
        WorkEntryItemViewModel? workItem = null,
        MissingBolItemViewModel? missingBolItem = null)
    {
        Kind = kind;
        Key = key;
        TypeLabel = typeLabel;
        Title = title;
        StatusText = statusText;
        ContextText = contextText;
        DateText = dateText;
        SemanticState = semanticState;
        WorkItem = workItem;
        MissingBolItem = missingBolItem;
    }

    public DriverAttentionKind Kind { get; }
    public string Key { get; }
    public string TypeLabel { get; }
    public string Title { get; }
    public string StatusText { get; }
    public string ContextText { get; }
    public string DateText { get; }
    public SemanticState SemanticState { get; }
    public WorkEntryItemViewModel? WorkItem { get; }
    public MissingBolItemViewModel? MissingBolItem { get; }
}

public sealed class DriverWorkspaceViewModel : WorkspaceViewModel
{
    public DriverWorkspaceViewModel(
        DriverRowViewModel driver,
        IReadOnlyList<DriverAttentionItemViewModel> needsAttention,
        IReadOnlyList<WorkEntryItemViewModel> recentActivity,
        DriverWorkspaceFocus focus,
        long? highlightedWorkEntryId = null)
        : base(
            WorkspaceRoute.DriverWorkspace,
            driver.DriverName,
            $"Fleet > {driver.DriverName}",
            "Back to Fleet")
    {
        Driver = driver;
        NeedsAttention = needsAttention;
        RecentActivity = recentActivity;
        Focus = focus;
        HighlightedWorkEntryId = highlightedWorkEntryId;
    }

    public DriverRowViewModel Driver { get; }
    public IReadOnlyList<DriverAttentionItemViewModel> NeedsAttention { get; }
    public IReadOnlyList<WorkEntryItemViewModel> RecentActivity { get; }
    public DriverWorkspaceFocus Focus { get; }
    public long? HighlightedWorkEntryId { get; }
    public bool HasNeedsAttention => NeedsAttention.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public string EmptyStateText => "No work currently needs attention for this driver.";
    public string ActivityEmptyText => "No activity recorded today.";
    public string FocusDescription => Focus switch
    {
        DriverWorkspaceFocus.MissingBol => "Missing BOL work is in focus.",
        DriverWorkspaceFocus.OpenWork => "Open manual work is in focus.",
        _ => string.Empty
    };
    public bool HasFocusDescription => FocusDescription.Length > 0;
}

public sealed class IdleTaskWorkspaceViewModel : WorkspaceViewModel
{
    public IdleTaskWorkspaceViewModel(DriverRowViewModel driver)
        : base(
            WorkspaceRoute.IdleTask,
            "Idle Contact",
            $"Fleet > {driver.DriverName} > Idle",
            "Back to Driver")
    {
        Driver = driver;
    }

    public DriverRowViewModel Driver { get; }
    public string ThresholdDisplay => $"{Driver.Threshold.ToString("0.0", CultureInfo.CurrentCulture)}%";
    public string ReportCycleDisplay => Driver.Record.ReportCycleDate.ToString("M/d/yyyy", CultureInfo.CurrentCulture);
}

public sealed class MissingBolActionHistoryItemViewModel
{
    public MissingBolActionHistoryItemViewModel(MissingBolActionRecord record)
    {
        Record = record;
    }

    public MissingBolActionRecord Record { get; }
    public string OutcomeDisplay => Record.Outcome == MissingBolActionOutcome.FollowUp
        ? "Follow-up"
        : Record.Outcome.ToString();
    public string CreatedDisplay => Record.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string NoteDisplay => string.IsNullOrWhiteSpace(Record.Note) ? "No note" : Record.Note;
    public string ContextDisplay =>
        $"Unit {DisplayOrUnavailable(Record.UnitCodeSnapshot)}  •  Leader {DisplayOrUnavailable(Record.DriverLeaderSnapshot)}";
    public SemanticState SemanticState => Record.Outcome switch
    {
        MissingBolActionOutcome.Resolved => SemanticState.Completed,
        MissingBolActionOutcome.Reopen => SemanticState.Warning,
        MissingBolActionOutcome.FollowUp => SemanticState.FollowUp,
        MissingBolActionOutcome.Attempted => SemanticState.Warning,
        _ => SemanticState.Information
    };

    private static string DisplayOrUnavailable(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not supplied" : value;
}

public sealed class MissingBolTaskWorkspaceViewModel : WorkspaceViewModel
{
    public MissingBolTaskWorkspaceViewModel(
        DriverRowViewModel driver,
        MissingBolItemViewModel item,
        IReadOnlyList<MissingBolActionHistoryItemViewModel> history)
        : base(
            WorkspaceRoute.MissingBolTask,
            $"Missing BOL — Order {item.OrderNumber}",
            $"Fleet > {driver.DriverName} > Missing BOL > {item.OrderNumber}",
            "Back to Driver")
    {
        Driver = driver;
        Item = item;
        History = history;
    }

    public DriverRowViewModel Driver { get; }
    public MissingBolItemViewModel Item { get; }
    public IReadOnlyList<MissingBolActionHistoryItemViewModel> History { get; }
    public bool HasHistory => History.Count > 0;
}

public sealed class WorkItemTaskWorkspaceViewModel : WorkspaceViewModel
{
    public WorkItemTaskWorkspaceViewModel(DriverRowViewModel driver, WorkEntryItemViewModel item)
        : base(
            WorkspaceRoute.WorkItemTask,
            "Work Item",
            $"Fleet > {driver.DriverName} > Work Item",
            "Back to Driver")
    {
        Driver = driver;
        Item = item;
    }

    public DriverRowViewModel Driver { get; }
    public WorkEntryItemViewModel Item { get; }
}

public sealed class NewWorkWorkspaceViewModel : WorkspaceViewModel
{
    public NewWorkWorkspaceViewModel(DriverRowViewModel driver)
        : base(
            WorkspaceRoute.NewWork,
            "Add Work",
            $"Fleet > {driver.DriverName} > Add Work",
            "Back to Driver")
    {
        Driver = driver;
    }

    public DriverRowViewModel Driver { get; }
}

public sealed class ActivityDetailWorkspaceViewModel : WorkspaceViewModel
{
    public ActivityDetailWorkspaceViewModel(DriverRowViewModel driver, WorkEntryItemViewModel item)
        : base(
            WorkspaceRoute.ActivityDetail,
            "Activity Detail",
            $"Fleet > {driver.DriverName} > Activity Detail",
            "Back to Driver")
    {
        Driver = driver;
        Item = item;
    }

    public DriverRowViewModel Driver { get; }
    public WorkEntryItemViewModel Item { get; }
}

public sealed class HandoffWorkspaceViewModel : WorkspaceViewModel
{
    public HandoffWorkspaceViewModel()
        : base(WorkspaceRoute.Handoff, "Shift Handoff", "Fleet > Handoff", "Back to Queue")
    {
    }
}

public sealed class UnmatchedBolWorkspaceViewModel : WorkspaceViewModel
{
    public UnmatchedBolWorkspaceViewModel(IReadOnlyList<MissingBolUnmatchedItemViewModel> items)
        : base(
            WorkspaceRoute.UnmatchedBol,
            "Unmatched Missing BOL",
            "Fleet > Unmatched Missing BOL",
            "Back to Queue")
    {
        Items = items;
    }

    public IReadOnlyList<MissingBolUnmatchedItemViewModel> Items { get; }
    public bool HasItems => Items.Count > 0;
}

public sealed class UnavailableWorkspaceViewModel : WorkspaceViewModel
{
    public UnavailableWorkspaceViewModel(string title, string breadcrumb, string message, string backLabel)
        : base(WorkspaceRoute.Unavailable, title, breadcrumb, backLabel)
    {
        Message = message;
    }

    public string Message { get; }
}
