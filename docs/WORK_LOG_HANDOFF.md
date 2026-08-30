# WAA Work Log + Handoff v0.2

This document is the authoritative product and technical specification for persistent driver work history and deterministic shift handoff.

## Driver-centric ownership

- Every work entry belongs to Driver Code.
- Driver Name is resolved for display.
- Unit Code and Driver Leader are snapshots of the context when the work occurred.
- Later report assignments never move, duplicate, or rewrite historical work.

## SQLite model and migration

`work_entries` contains:

- `id`
- `driver_code`
- `text`
- `status`
- `created_utc`
- `resolved_utc`, nullable
- `source`
- `linked_idle_contact_event_id`, nullable
- `report_cycle_date_snapshot`, nullable
- `unit_code_snapshot`
- `driver_leader_snapshot`

Allowed statuses:

- `Done`
- `Waiting`
- `FollowUp`

Allowed sources:

- `Manual`
- `IdleContact`

Semantics:

- Manual Done is resolved at creation.
- Waiting and FollowUp remain unresolved until explicitly resolved.
- `resolved_utc` is authoritative; resolution never changes the original status or text.
- Reopen clears only `resolved_utc` for Waiting or FollowUp.
- All stored timestamps are UTC; presentation uses the PC time zone.
- Work text is trimmed and may not be blank.
- Work entries are not destructively deleted in v0.2.

Indexes support:

- selected-driver history reads
- unresolved fleet counts
- created/resolved local-day range queries
- linked idle-event lookup

A partial unique index on `linked_idle_contact_event_id` prevents duplicate linked work.

Initialization creates the table/indexes non-destructively and backfills idle events that have no linked work entry. Backfill uses original event timestamps, metrics, coverage, note, cycle, unit, and leader snapshots. It is idempotent under repeated initialization.

A migration failure is logged and surfaced as a startup error. WAA never responds by deleting or replacing the existing database.

## Idle contact integration

The idle event and linked work entry save in the same transaction.

| Idle outcome | Work status | Resolution |
|---|---|---|
| Spoke | Done | event timestamp |
| Attempted | FollowUp | unresolved |
| Spoke — Follow-up | FollowUp | unresolved |

Generated text includes the event’s saved 28-day and 7-day snapshots. Incomplete 28-day coverage is reported as `28D incomplete n/4`; WAA does not invent a percentage. An optional note is appended once.

## Selected-driver workflow

The existing queue remains visible. The selected-driver pane is ordered as:

1. driver identity and idle context
2. current-cycle idle-contact controls
3. Open Work
4. New Work
5. Today’s Activity
6. Next Needing Attention

### Open Work

Open Work contains unresolved Waiting and FollowUp entries for the selected driver. Each item shows its status, local creation date/time, text, and Resolve action.

Resolving:

- sets `resolved_utc`
- preserves original status/text/creation/context
- removes the item from Open Work
- makes it eligible for Completed Today when resolved during the current local day

Reopen is available for resolved Waiting/FollowUp entries shown in Today’s Activity.

### New Work

One text field supplies three direct actions: Done, Waiting, and Follow-up.

- actions are disabled for blank/whitespace text
- save trims text
- duplicate submission is disabled while saving
- successful save clears the field and keeps the driver selected
- queue counts/order and selected-driver history refresh immediately
- failed save keeps the typed text available for retry

Draft text is retained per driver while switching selection during the current session.

### Today’s Activity

Today’s Activity uses the PC’s current local calendar-day boundary and displays newest-first for fast review.

It includes:

- work created today in any status
- linked idle-contact work created today
- older Waiting/FollowUp work resolved today

Idle events are not rendered separately, preventing duplicate activity lines.

## Fleet integration

Each row exposes a quiet Open Work count such as `2 open`; clear rows remain blank.

Counts are loaded by one aggregate indexed query as part of fleet state. Queue priority is:

1. above-threshold unfinished idle contact
2. above-threshold Spoke, with ordinary open work first
3. remaining ordinary open work
4. clear fleet

`Next Needing Attention` considers only visible drivers, prefers unfinished high-idle contact, then ordinary unresolved work, and leaves selection unchanged when no other visible driver qualifies.

## Handoff generation

Handoff is the only secondary top-level view and uses the same window.

Controls:

- Back to Queue
- Regenerate
- Copy to Clipboard
- editable multiline text
- generated section counts

The deterministic service consumes work-entry records plus an explicit local-day UTC range and returns text plus counts. It does not require WPF to run.

The output always contains:

1. `NEEDS FOLLOW-UP`
2. `WAITING / PENDING`
3. `COMPLETED TODAY`

Empty sections contain `None.` for consistent readability.

### Section membership

Needs Follow-up:

- unresolved FollowUp work, including linked Attempted and Spoke — Follow-up idle entries

Waiting / Pending:

- unresolved Waiting work

Completed Today:

- Done entries created today
- Waiting/FollowUp entries resolved today
- linked Spoke entries created today

An entry appears at most once per section calculation. Linked idle activity is not separately read from the idle-event table.

### Ordering and line format

- unresolved sections: oldest unresolved driver group first; entries chronological within each driver
- completed section: chronological completion/current-day activity order
- stable Driver Name/Driver Code/entry ID tie-breakers

Preferred line format:

`270139 — Jamie Example [ABC123]: Waiting on updated ETA.`

When the snapshot Unit Code is unavailable:

`Jamie Example [ABC123]: Waiting on updated ETA.`

Whitespace in entry text is collapsed for a concise operational line.

### Editor isolation

- entering Handoff or pressing Regenerate generates from saved work
- Regenerate intentionally replaces current editor text
- editing the draft never edits/resolves/reopens work or contact events
- Copy to Clipboard copies the editor’s current text, including user edits
- handoff is not continuously regenerated while the user types

## Theme and performance

All work and handoff controls use dynamic theme resources for light and dark appearance.

- no recurring timer or watcher
- no full database load on New Work keystrokes
- no per-row history queries
- selected-driver work loads only when selection/state changes
- database work executes off the UI thread through the focused view-model operations
- handoff generates only on entry or explicit Regenerate

## Privacy

Tests and fixtures use synthetic names, codes, leaders, units, paths, notes, and reports. Never commit production CSV/XLSX files, databases, logs, or real employee data.

## Validation coverage

Automated Windows validation covers:

- all manual statuses and restart persistence
- resolve and reopen semantics
- idle outcome mappings and atomic rollback
- incomplete metric wording
- legacy migration/backfill and repeated initialization
- aggregate open counts and historical snapshots
- priority bands and Next behavior
- local-day activity and handoff boundaries
- section placement, ordering, deduplication, and missing-unit formatting
- editable copy behavior and repository isolation
- failed-save draft retention
- threshold persistence, dark-mode persistence, ten-character leader round trip
- existing parser and weighted-idle behavior
- WPF/XAML build and self-contained win-x64 publish
