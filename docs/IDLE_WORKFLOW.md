# WAA Idle Workflow Specification

## Purpose and cycle key

Fleet Queue shows current weighted idle context, whether the current-cycle idle conversation is finished, ordinary unresolved work, and who needs attention next. The queue is the full-width home workspace; driver and idle work open inside the same MainWindow central content host.

The accepted report’s maximum normalized `Week Start Date` is stable `ReportCycleDate`. Idle contact state is keyed by Driver Code + Report Cycle Date.

- corrected report with same cycle preserves contact state
- newer cycle derives fresh `Not Contacted` without deleting history
- Unit/Leader changes never split driver identity or rewrite snapshots
- Driver Workspace/Idle Task routes use durable Driver Code, never Unit Code

## Weighted calculations

### Driver 7-day

`Idle7d = IdleHours7d / EngineHours7d × 100`

Zero denominator displays `N/A`.

### Driver 28-day

Use current period and exact periods 7, 14, and 21 days earlier:

`Idle28d = Sum(IdleHours) / Sum(EngineHours) × 100`

Never average weekly percentages. All four expected observations are required for a complete value. Missing periods display incomplete coverage; zero total denominator displays `N/A`.

### Fleet values

Fleet 7-day/28-day values are summed numerator/denominator calculations and expose included-driver coverage. Fleet 28-day includes current-roster drivers with complete four-period coverage.

## Threshold

- default `50.0%`
- valid range `0.0` through `100.0`
- strict greater-than comparison
- same threshold applies to valid 7-day and complete 28-day values
- either value above threshold puts driver in high-idle population
- threshold change reranks immediately and never rewrites contact/work history
- if changed below Fleet Queue, current valid workspace is rebuilt rather than route context discarded

## Contact outcomes

- `Not Contacted` — no event current cycle
- `Attempted` — driver not reached; actionable
- `Spoke` — current-cycle idle conversation complete
- `Spoke — Follow-up` — driver reached; further action remains

Each event snapshots Driver Code/cycle, UTC time, outcome/note, weighted 7-day, weighted 28-day or incomplete coverage, threshold, Unit, Leader, and source import ID.

## Automatic idle-to-work linkage

New idle event and linked work entry save in one atomic SQLite transaction.

- `Spoke` → `Done`, resolved at event timestamp
- `Attempted` → `FollowUp`, unresolved
- `Spoke — Follow-up` → `FollowUp`, unresolved

Persisted generated work text uses event metric snapshots and optional note. A partial unique index permits at most one work entry per linked event. Initialization idempotently backfills older events that predate work-log linkage.

## Queue ordering

Required bands:

1. **Above threshold, idle unfinished** — Not Contacted / Attempted / Spoke — Follow-up.
2. **Above threshold, Spoke** — unresolved work before clear within band.
3. **Remaining unresolved work**, including Missing BOL tasks.
4. **Remaining clear drivers.**

Within band 1:

1. Spoke — Follow-up
2. Attempted
3. Not Contacted
4. highest current idle concern using larger valid 7-day/complete 28-day value
5. Driver Name / Driver Code stable tie-breakers

Queue unresolved counts are aggregate/indexed reads, not one history query per row. Fleet rows remain virtualized/recycling.

## Driver Workspace presentation

The old always-visible selected-driver pane is removed. Single click on Fleet row (or focus + Enter) opens Driver Workspace inside MainWindow.

Driver Workspace shows identity, Unit/Leader, report cycle, weighted 28-day/7-day values, current idle-contact state, Open Work/BOL counts, Needs Attention, Quick Actions, and compact Today’s Activity.

When current idle work needs attention, Driver Workspace presents exactly one idle attention row. The linked work entry is not rendered as another manual actionable row.

Opening it navigates to focused Idle Task workspace.

## Idle Task behavior

Idle Task shows Driver identity, current Unit/Leader, report cycle, weighted 28-day value/coverage, weighted 7-day value, threshold, current-cycle outcome, prior note where available, optional action note, Spoke / Attempted / Spoke — Follow-up, Next Work Item, and Next Needing Attention.

Saving an idle outcome:

1. inserts event + linked work atomically
2. clears successfully saved optional note
3. refreshes queue/driver/current task state
4. preserves current Idle Task route rather than silently jumping
5. updates status text with saved outcome

User then explicitly Backs, chooses Next Work Item, or Next Needing Attention. The same idle conversation is never retyped as ordinary work.

## Next Work Item / Next Needing Attention

For one driver, unfinished idle contact is first Next Work Item priority. Missing BOL and manual work follow `docs/CENTRAL_WORKSPACE.md` ordering.

Next Needing Attention examines only current visible/search-filtered queue:

- prefer another unfinished high-idle driver
- otherwise another driver with unresolved ordinary work
- never jump to driver hidden by search
- if none qualify, retain context and report clearly

From a task/driver workspace, moving to another driver opens that driver’s central Driver Workspace, never another window.

## Report update behavior

- show saved data first
- scan/import once during launch
- thereafter only explicit `Update Reports`
- no FileSystemWatcher/recurring scan/polling timer
- same-cycle update refreshes assignments/metrics while preserving contacts
- new-cycle update preserves history and derives new pending state
- rejected report retains last-known-good roster/contacts/work/settings/BOL/appearance
- while navigated, rebuild current valid route using stable Driver Code/item IDs
- preserve unsaved notes; stale route becomes explicit Unavailable state rather than crash

## v0.4.2 Handoff behavior

Idle activity enters Handoff only through its linked work entry; `idle_contact_events` is never rendered separately, so the same conversation cannot duplicate.

The **runtime compact Handoff** no longer exposes the old `NEEDS FOLLOW-UP` / `COMPLETED TODAY` headings. Instead it creates at most one alphabetical narrative line per driver.

For linked idle work:

- unresolved Attempted and Spoke — Follow-up still qualify as actionable saved work
- current-day Spoke still qualifies as current-day completed activity
- underlying event/work classification and local-day boundaries remain deterministic
- visible Handoff prose uses a concise idle action phrase and preserves the human-entered note
- generated 28D/7D metric boilerplate is intentionally omitted from the copied narrative because the saved metric snapshots remain available in the Idle Task/history
- WAA does not invent a coached/not-coached state because no such field is stored

Handoff prefers current fleet Unit/Driver Name when available, while the historical linked work retains its original assignment/metric snapshots internally.

Editing/copying Handoff never mutates idle state. Navigating away/back preserves edited draft; Regenerate intentionally rebuilds from saved work.

## Acceptance criteria

- weighted 28-day uses summed raw hours and requires four expected observations
- threshold change reranks without rewriting history
- unfinished high-idle work ranks before completed high-idle and ordinary work
- idle event + linked work is atomic
- legacy backfill is repeatable without duplication
- one idle attention item represents linked current work in Driver Workspace
- saving idle refreshes state without unexpected route jump
- same-cycle correction preserves contact state
- new cycle creates fresh pending state without deleting history
- Next respects search and prioritizes idle attention
- Fleet → Driver → Idle route is keyboard-accessible inside one MainWindow
- compact Handoff does not duplicate idle events or repeat metric boilerplate
- no report watcher or periodic polling exists
