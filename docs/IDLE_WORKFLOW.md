# WAA Idle Workflow Specification

## Purpose and cycle key

Fleet Queue shows current weighted idle context, whether the current-cycle idle conversation is finished, ordinary unresolved work, and who needs attention next. In v0.4 the queue is the full-width home workspace; driver and idle work open inside the same MainWindow central content host.

The accepted report’s maximum normalized `Week Start Date` is the stable `ReportCycleDate`. Idle contact state is keyed by Driver Code + Report Cycle Date.

- A corrected report with the same cycle preserves contact state.
- A newer cycle derives fresh `Not Contacted` state without deleting prior events.
- Unit or leader changes never split driver identity or rewrite snapshots.
- Driver Workspace/Idle Task routes use durable Driver Code rather than Unit Code.

## Weighted calculations

### Driver 7-day

`Idle7d = IdleHours7d / EngineHours7d × 100`

A zero denominator displays `N/A`.

### Driver 28-day

Use the current period and the exact periods 7, 14, and 21 days earlier:

`Idle28d = Sum(IdleHours) / Sum(EngineHours) × 100`

Never average weekly percentages. All four expected observations are required for a complete value. Missing periods display incomplete coverage such as `3/4`; a zero total denominator displays `N/A`.

### Fleet values

Fleet 7-day and 28-day values are also summed numerator/denominator calculations and expose included-driver coverage. Fleet 28-day includes current-roster drivers with complete four-period coverage.

## Threshold

- default `50.0%`
- valid range `0.0` through `100.0`
- strict greater-than comparison
- one threshold applies to valid 7-day and complete 28-day values
- either value above threshold puts the driver in the high-idle population
- changing threshold reranks immediately and never changes saved contact/work history
- if threshold changes while below Fleet Queue, WAA rebuilds the current valid workspace rather than discarding route context

## Contact outcomes

- `Not Contacted` — no event for the current cycle
- `Attempted` — driver not reached; actionable
- `Spoke` — current-cycle idle conversation complete
- `Spoke — Follow-up` — driver reached, further action remains open

Each event snapshots:

- Driver Code and report cycle
- UTC timestamp
- outcome and optional note
- weighted 7-day percentage
- weighted 28-day percentage or incomplete coverage
- threshold
- Unit Code
- Driver Leader
- source import ID

## Automatic idle-to-work linkage

A new idle event and its linked work entry are one atomic SQLite transaction. Either both save or neither saves.

Mappings:

- `Spoke` → `Done`, resolved at event timestamp
- `Attempted` → `FollowUp`, unresolved
- `Spoke — Follow-up` → `FollowUp`, unresolved

Generated work text uses event metric snapshots, not later recalculation. Incomplete 28-day coverage is stated explicitly. Optional note text is appended concisely.

A partial unique index permits at most one work entry per linked idle event. Initialization idempotently backfills older idle events that predate the work-log feature.

## Queue ordering

The queue uses these required bands:

1. **Above threshold, idle unfinished** — `Not Contacted`, `Attempted`, or `Spoke — Follow-up`.
2. **Above threshold, Spoke** — drivers with ordinary unresolved work first within this band.
3. **Remaining drivers with unresolved ordinary work**, including Missing BOL tasks.
4. **Remaining clear drivers.**

Within band 1:

1. `Spoke — Follow-up`
2. `Attempted`
3. `Not Contacted`
4. highest current idle concern, using the larger valid 7-day/complete 28-day value
5. Driver Name and Driver Code as stable tie-breakers

The queue obtains unresolved counts with aggregate/indexed reads, not one history query per row. Fleet rows remain virtualized/recycling.

## v0.4 Driver Workspace presentation

The old always-visible selected-driver pane is removed. A single click on a Fleet Queue row (or keyboard focus + Enter) opens Driver Workspace inside MainWindow.

Driver Workspace is a work index. It shows Driver Name/Code, Unit Code, Driver Leader, current report cycle, weighted 28-day and 7-day values, current idle-contact state, Open Work count, Missing BOL count, Needs Attention, Quick Actions, and compact Today’s Activity.

When current idle work needs attention, Driver Workspace presents **one** idle attention row. The linked work entry is not rendered as a second manual actionable row.

Opening the idle row navigates to a focused Idle Task workspace.

## Idle Task behavior

Idle Task shows:

- Driver identity
- Unit Code and Driver Leader context
- report cycle
- weighted 28-day value and coverage
- weighted 7-day value
- current threshold
- current-cycle outcome
- previous/current-cycle note where available
- one optional action note editor
- `Spoke`
- `Attempted`
- `Spoke — Follow-up`
- `Next Work Item`
- `Next Needing Attention`

Saving an idle outcome:

1. inserts the event and linked work atomically
2. clears the successfully saved optional note
3. refreshes queue/driver/current task state
4. preserves the current Idle Task route rather than silently jumping to another driver
5. updates status text with the saved outcome

The user can then explicitly Back to Driver, choose Next Work Item, or choose Next Needing Attention. The user never retypes the same idle conversation as ordinary work.

## Next Work Item and Next Needing Attention

For one driver, unfinished idle contact is the first `Next Work Item` priority. Missing BOL and manual work follow according to `docs/CENTRAL_WORKSPACE.md`.

`Next Needing Attention` examines only the current visible/search-filtered fleet queue:

- prefer another driver with unfinished high-idle contact work
- otherwise choose another driver with unresolved ordinary work
- do not jump to a driver hidden by search
- if no other visible driver needs attention, retain current context and report that clearly

When invoked from a task/driver workspace and another driver is selected, WAA opens that driver's central Driver Workspace rather than spawning another window.

## Report update behavior

- show saved data first
- scan/import once during launch
- thereafter update only through `Update Reports`
- no `FileSystemWatcher`, recurring scan, or polling timer
- same-cycle updates refresh assignments/metrics while preserving contacts
- new-cycle updates preserve history and naturally derive new pending state
- rejected reports retain last-known-good roster, contacts, work, settings, BOL state, and appearance
- while navigated, rebuild the current valid route through stable Driver Code/item IDs
- preserve unsaved local notes; stale route entities fail to an explicit Unavailable state rather than crash

## Handoff behavior

Idle activity is rendered only through its linked work entry:

- unresolved Attempted and Spoke — Follow-up entries appear in `NEEDS FOLLOW-UP`
- current-day Spoke entries appear in `COMPLETED TODAY`
- no event is rendered a second time from `idle_contact_events`
- metric and assignment context comes from historical snapshots

Handoff is a central workspace route in the same MainWindow. Editing/copying it does not mutate idle state. Navigating away/back preserves the edited draft during the session; Regenerate intentionally replaces it from saved work.

## Acceptance criteria

- weighted 28-day uses summed raw hours and requires four expected observations
- threshold changes immediately rerank without rewriting history
- unfinished high-idle work ranks before completed high-idle and ordinary work
- idle event + linked work insertion is atomic
- legacy event backfill is repeatable without duplication
- one idle attention item represents the linked current work in Driver Workspace
- saving idle action refreshes current state without an unexpected automatic route jump
- same-cycle correction preserves contact state
- new cycle creates fresh pending state without deleting history
- Next respects search and prefers idle attention
- Fleet → Driver → Idle route is keyboard-accessible inside one MainWindow
- no report watcher or periodic polling exists
