# WAA Work Log + Handoff v0.3

This document is the authoritative product and technical specification for persistent driver work history and deterministic shift handoff, including Missing BOL task/action integration.

## Driver-centric ownership

- Every work entry belongs to durable Driver Code.
- Driver Name is resolved for display.
- Unit Code and Driver Leader are snapshots of context when work occurred.
- Later roster assignments never move, duplicate, or rewrite historical work.
- Missing BOL source names and source leaders never replace durable WAA identity/context.

## SQLite work model and migration

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

Allowed work statuses:

- `Done`
- `Waiting`
- `FollowUp`

Base persisted sources remain compatible with the v0.2 table:

- `Manual`
- `IdleContact`

Missing BOL semantic sources are recorded through `missing_bol_work_links` and overlaid when records are loaded:

- `MissingBolTask`
- `MissingBolAction`

This preserves the existing work table while providing exact task/action provenance, source import ID, and Missing BOL item linkage.

Semantics:

- Manual Done is resolved at creation.
- Waiting and FollowUp remain unresolved until explicitly resolved.
- `resolved_utc` is authoritative; resolution never erases original status, text, creation time, or context snapshots.
- Reopen clears only `resolved_utc` for ordinary Waiting/FollowUp work.
- A MissingBolTask is a FollowUp entry that remains unresolved until its item is explicitly resolved.
- A MissingBolAction is a Done entry resolved at action time.
- All timestamps are stored in UTC; presentation uses the PC time zone.
- Work text is trimmed and may not be blank.
- Work entries are not destructively deleted in v0.3.

Indexes support selected-driver history, aggregate unresolved fleet counts, local-day activity/handoff queries, idle links, BOL item/task links, and action-source links.

Initialization is non-destructive. It creates current tables/indexes, idempotently backfills legacy idle events without linked work, and adds separate Missing BOL tables/links. A migration failure is logged and surfaced as a startup error. WAA never deletes or replaces the existing database in response.

## Idle contact integration

The idle event and linked work entry save in the same transaction.

| Idle outcome | Work status | Resolution |
|---|---|---|
| Spoke | Done | event timestamp |
| Attempted | FollowUp | unresolved |
| Spoke — Follow-up | FollowUp | unresolved |

Generated text uses the event’s saved 28-day/7-day snapshots. Incomplete coverage is reported as `28D incomplete n/4`; WAA never invents a percentage. Optional note text is appended once.

## Missing BOL task integration

For every newly matched unresolved Missing BOL item without a task, WAA creates exactly one linked FollowUp work entry in the same import transaction.

Task text contains available source context without broken punctuation:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots:

- matched Driver Code
- Unit Code at task creation
- Driver Leader at task creation
- current report cycle when available
- task creation UTC
- Missing BOL item ID through the link table
- source Missing BOL import ID

Uniqueness constraints prevent one item from owning more than one task. Reimport and Reopen reuse the same task.

When source context or local BOL status changes, the linked task text is updated while its original creation time and context snapshots remain intact. Disappearance from the latest source does not resolve or delete the task.

## Missing BOL action integration

Each Requested, Attempted, Follow-up, Resolved, or Reopen operation appends one Missing BOL action event and creates one linked completed activity work entry.

| Action | Item state | Task state | Activity text |
|---|---|---|---|
| Requested | Requested | unresolved | `Requested missing BOL for order …` |
| Attempted | Attempted | unresolved | `Attempted contact regarding missing BOL for order …; driver not reached.` |
| Follow-up | FollowUp | unresolved | `Missing BOL for order … requires follow-up.` |
| Resolved | Resolved + timestamp | resolved at same timestamp | `Resolved missing BOL for order …` |
| Reopen | Open, resolution cleared | same task reopened | `Reopened missing BOL for order …` |

Optional note text is appended concisely to the activity entry and retained in action history.

Atomic boundaries:

- Requested/Attempted/Follow-up: item status + task text/state + action event + completed activity
- Resolved: item resolution + task resolution + action event + completed activity
- Reopen: item reopen + same task reopen + action event + completed activity

Every group commits fully or rolls back fully. A linked action event has exactly one activity work entry. The UI prevents duplicate submission while an item action is saving and retains note text after a failed save.

## Consistency with general Open Work

The linked MissingBolTask appears once in Open Work because it is a real unresolved work entry. It is not separately duplicated from the BOL table.

Current v0.3 coherence rule:

- Resolve/Reopen buttons are hidden for MissingBolTask entries in general Open Work.
- The card instructs the user to use the Missing BOL actions above.
- Database guards reject generic task resolution that would bypass BOL item/action state.

This deliberate behavior prevents task/item drift. Ordinary manual and idle FollowUp entries keep their normal Resolve/Reopen controls.

## Selected-driver workflow

The queue remains visible. The selected-driver pane is ordered as:

1. driver identity and idle context
2. current-cycle idle-contact controls
3. Missing BOL
4. Open Work
5. New Work
6. Today’s Activity
7. Next Needing Attention

### Missing BOL

The section loads only when selection or saved BOL state changes. It supports multiple items and orders unresolved before resolved items. Each compact card shows Order #, Empty Call Date, route, state, optional customer/miles, exact source Driver Code/name evidence, source-presence warning, optional note, and direct actions.

Resolved items remain visible for review and Reopen. A resolved item present again shows a restrained warning. An unresolved item missing from the newest source shows `Not in latest report` and remains actionable.

### Open Work

Open Work contains unresolved Waiting and FollowUp entries, including one MissingBolTask per matched unresolved item. Each item shows status, local creation date/time, source, text, and the appropriate control/instruction.

Resolving ordinary work:

- sets `resolved_utc`
- preserves original status/text/creation/context
- removes it from Open Work
- makes it eligible for Completed Today when resolved during the current local day

### New Work

One text field supplies Done, Waiting, and Follow-up actions.

- disabled for blank/whitespace text
- trims text on save
- disables duplicate submission while saving
- clears after successful save and keeps driver selected
- immediately refreshes queue/history
- retains typed text after failure
- retains per-driver drafts during selection changes in the current session

### Today’s Activity

Today’s Activity uses the PC’s local calendar-day boundary and displays newest-first.

It includes:

- manual work created today
- linked idle-contact work created today
- older ordinary work resolved today
- MissingBolAction entries created today

It excludes MissingBolTask entries as completed activity; resolving a task is represented by the linked Resolved action instead. Idle and BOL event tables are not separately rendered, preventing duplicates.

## Fleet integration

Each row exposes:

- Open Work count: all unresolved Waiting/FollowUp work, including MissingBolTask
- BOL count: unresolved matched Missing BOL subset

Both are loaded through aggregate indexed queries, never one query per driver. Order # text is aggregated for deterministic fleet search.

Queue priority remains:

1. above-threshold unfinished idle contact
2. above-threshold Spoke, with unresolved work first
3. remaining unresolved work, including Missing BOL
4. clear fleet

Within otherwise equal ordinary unresolved work, oldest open Missing BOL Empty Call Date may break ties. `Next Needing Attention` considers only visible drivers, prefers unfinished high-idle contact, then unresolved work, and leaves selection unchanged when no other visible driver qualifies.

## Handoff generation

Handoff remains the only secondary top-level view in the same window.

Controls:

- Back to Queue
- Regenerate
- Copy to Clipboard
- editable multiline text
- generated section counts

The deterministic service consumes work-entry records plus an explicit local-day UTC range and returns text/counts without requiring WPF launch.

The output always contains:

1. `NEEDS FOLLOW-UP`
2. `WAITING / PENDING`
3. `COMPLETED TODAY`

Empty sections contain `None.`.

### Section membership

Needs Follow-up:

- unresolved FollowUp work
- linked Attempted and Spoke — Follow-up idle entries
- unresolved MissingBolTask entries

Waiting / Pending:

- unresolved Waiting work

Completed Today:

- Done entries created today
- ordinary Waiting/FollowUp entries resolved today
- linked Spoke entries created today
- MissingBolAction entries created today

A resolved MissingBolTask is excluded from Completed Today so the Resolved action is the single activity line. An entry appears at most once per section calculation.

### Ordering and line format

- unresolved sections: oldest unresolved driver group first; entries chronological within driver
- completed section: chronological action/completion order
- stable Driver Name, Driver Code, and entry ID tie-breakers

Preferred line format:

`270139 — Jamie Example [ABC123]: Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Requested.`

When snapshot Unit Code is unavailable:

`Jamie Example [ABC123]: Waiting on updated ETA.`

Whitespace is collapsed for concise operational lines.

### Editor isolation

- entering Handoff or pressing Regenerate generates from saved work
- Regenerate intentionally replaces current editor text
- editing never mutates work, BOL items/actions, idle events, reports, or settings
- Copy to Clipboard copies current editor text, including user edits
- handoff is not continuously regenerated while typing

## Theme, performance, and privacy

All new and existing controls use dynamic theme resources for light/dark appearance.

- no recurring timer or watcher
- no Excel or Office process
- no database call on note/new-work keystrokes
- no per-row BOL/history queries
- selected-driver work/BOL loads only when selection/state changes
- database and source parsing runs off the UI thread through focused operations
- handoff generates only on entry or explicit Regenerate

Tests and fixtures use synthetic names, codes, leaders, units, orders, routes, customers, paths, notes, and generated workbooks. Never commit production CSV/XLSX files, databases, logs, or screenshots.

## Validation coverage

The Windows suite covers parser/cell formats, exact matching, unmatched handling, imports/idempotency/source lifecycle, task uniqueness/snapshots/restart, all action mappings and rollback, migration preservation/failure, aggregate fleet counts, queue/search/Next behavior, selected-driver multiple items, local-day work/handoff lifecycle, deduplication, editor isolation, failed-save retention, duplicate-submit prevention, threshold/theme/ten-character-leader regressions, WPF/XAML compilation, and self-contained Windows x64 publishing.
