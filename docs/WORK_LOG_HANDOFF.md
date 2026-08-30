# WAA Work Log + Handoff v0.4 Presentation / v0.3 Data Contract

This document is authoritative for persistent driver work history and deterministic shift Handoff, including idle linkage and Missing BOL task/action integration. v0.4 replaces the old selected-driver split-pane presentation with central Driver/Task workspaces while preserving the validated work/history model.

## Driver-centric ownership

- Every work entry belongs to durable Driver Code.
- Driver Name is display identity.
- Unit Code and Driver Leader are snapshots/context, not identity.
- Later roster assignments never move, duplicate, or rewrite historical work.
- Missing BOL source names/leaders never replace durable WAA identity/context.
- Driver/task routes use durable Driver Code and persisted record IDs rather than Unit Code.

## SQLite work model

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

Base persisted sources remain compatible with the existing table:

- `Manual`
- `IdleContact`

Missing BOL semantic sources are overlaid through `missing_bol_work_links`:

- `MissingBolTask`
- `MissingBolAction`

Semantics:

- Manual Done is resolved at creation.
- Waiting/FollowUp remain unresolved until explicit Resolve.
- `resolved_utc` is authoritative; resolution never erases original status/text/creation/context snapshots.
- Reopen clears only `resolved_utc` for supported ordinary Waiting/FollowUp work.
- MissingBolTask remains unresolved until synchronized BOL Resolve.
- MissingBolAction is a completed Done activity at action time.
- timestamps store UTC; presentation uses PC time zone
- work text is trimmed/non-blank
- work entries are not destructively deleted

Indexes support driver history, aggregate unresolved fleet counts, local-day activity/Handoff, idle links, BOL item/task links, and action-source links.

Initialization remains non-destructive. v0.4 central navigation/theming introduces no schema migration and does not increment schema version.

## Idle contact integration

Idle event and linked work entry save in one transaction.

| Idle outcome | Work status | Resolution |
|---|---|---|
| Spoke | Done | event timestamp |
| Attempted | FollowUp | unresolved |
| Spoke — Follow-up | FollowUp | unresolved |

Generated work text uses saved metric snapshots. Incomplete 28-day coverage is represented explicitly; optional note text is appended once.

In v0.4 Driver Workspace, the current actionable idle state appears once as an Idle attention item rather than also rendering its linked work as a separate manual row. Persisted linkage/Handoff behavior is unchanged.

## Missing BOL task integration

Every newly matched unresolved Missing BOL item without a task creates exactly one linked FollowUp work entry in the same import transaction.

Example persisted task text:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots matched Driver Code, Unit Code, Driver Leader, report cycle when available, creation UTC, BOL item linkage, and source import.

Uniqueness constraints prevent one item from owning more than one task. Reimport/Reopen reuse the same task. Source/status wording may update while original creation/context snapshots stay intact. Source disappearance does not resolve/delete the task.

Driver Workspace represents each unresolved BOL item once as a Missing BOL attention row. It does not duplicate the linked MissingBolTask as manual work. Generic Work Item Resolve/Reopen remains unavailable for MissingBolTask, and database guards reject bypass state changes.

## Missing BOL action integration

Each Requested, Attempted, Follow-up, Resolved, or Reopen appends one BOL action event and creates one linked completed activity entry.

| Action | Item state | Task state | Activity text |
|---|---|---|---|
| Requested | Requested | unresolved | `Requested missing BOL for order …` |
| Attempted | Attempted | unresolved | `Attempted contact regarding missing BOL for order …; driver not reached.` |
| Follow-up | FollowUp | unresolved | `Missing BOL for order … requires follow-up.` |
| Resolved | Resolved + timestamp | resolved same timestamp | `Resolved missing BOL for order …` |
| Reopen | Open, resolution cleared | same task reopened | `Reopened missing BOL for order …` |

Optional notes are retained in action history and appended concisely to activity text.

Atomic boundaries remain:

- Requested/Attempted/Follow-up: item status + task text/state + action event + completed activity
- Resolved: item resolution + task resolution + action event + completed activity
- Reopen: item reopen + same-task reopen + action event + completed activity

Every group commits fully or rolls back fully. Duplicate submit is blocked while save is active. Failed save retains note for retry; v0.4 also retains unsaved BOL note drafts across route/report refreshes.

## v0.4 Driver Workspace work index

The Fleet Queue no longer remains beside an always-open driver card. A row opens a full Driver Workspace in the same MainWindow.

Driver Workspace shows summary plus `NEEDS ATTENTION`, Quick Actions, and compact Today’s Activity. Actionable work is represented once:

1. unfinished idle contact
2. each unresolved Missing BOL item
3. each unresolved manual Waiting/FollowUp item

The page does not expose all editors simultaneously. Each row opens a focused task workspace.

### Manual Work Item workspace

A manual work item shows:

- Driver identity
- original status
- text
- created local date/time
- source
- Unit snapshot
- Driver Leader snapshot
- report-cycle snapshot
- resolution state/time
- Resolve/Reopen when allowed

MissingBolTask instructions point back to the synchronized Missing BOL workspace and cannot use generic resolution.

### New Work workspace

`Add Work` opens a focused page with one multiline editor and:

- Done
- Waiting
- Follow-up

Rules remain:

- blank/whitespace input cannot save
- input is trimmed
- duplicate submission disabled while saving
- successful save clears persisted draft and refreshes fleet/driver work
- failed save retains typed text
- per-driver draft survives in-session navigation
- Unit/Leader/report-cycle snapshots are taken from current driver context

v0.4 successful New Work returns to the same Driver Workspace and highlights/retains context for the newly saved entry.

### Today’s Activity and Activity Detail

Today’s Activity uses PC local calendar-day boundaries and newest-first display.

It includes:

- manual work created today
- linked idle-contact work created today
- older ordinary work resolved today
- MissingBolAction entries created today

It excludes MissingBolTask as completed activity; BOL Resolve is represented by its linked Resolved action. Event tables are not separately rendered, preventing duplicates.

A compact activity row may open read-only Activity Detail. Activity Detail provides context only and creates no edit/delete path.

## Fleet integration

Fleet rows expose:

- Open Work count: all unresolved Waiting/FollowUp work including MissingBolTask
- BOL count: unresolved matched Missing BOL subset

Counts are aggregate/indexed, never one query per driver. Order # text is aggregated for deterministic search.

Queue priority remains:

1. above-threshold unfinished idle contact
2. above-threshold Spoke with unresolved work before clear
3. remaining unresolved work including Missing BOL
4. clear fleet

Within otherwise equal ordinary unresolved work, oldest open Missing BOL Empty Call Date may break ties. `Next Needing Attention` considers visible/search-filtered drivers only.

## Next Work Item

v0.4 adds direct `Next Work Item` on Driver/Task workspaces. It orders one driver’s action list:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual FollowUp, oldest first
4. manual Waiting, oldest first
5. other supported unresolved manual work

It advances through that same list rather than creating another repository/priority engine. When the driver has no next item, WAA reuses existing search-respecting `Next Needing Attention`.

## Handoff generation

Handoff is a focused full-width route in the same MainWindow central content host, not another operating-system Window.

Controls:

- Back to Queue
- Regenerate
- Copy to Clipboard
- editable multiline draft
- generated section counts

The deterministic service consumes saved work-entry records plus explicit local-day UTC range and does not require WPF launch.

Output always contains:

1. `NEEDS FOLLOW-UP`
2. `WAITING / PENDING`
3. `COMPLETED TODAY`

Empty sections contain `None.`.

### Section membership

Needs Follow-up:

- unresolved FollowUp
- linked Attempted and Spoke — Follow-up idle entries
- unresolved MissingBolTask

Waiting / Pending:

- unresolved Waiting

Completed Today:

- Done created today
- ordinary Waiting/FollowUp resolved today
- linked Spoke created today
- MissingBolAction created today

Resolved MissingBolTask is excluded from Completed Today so the Resolved action remains the single completion line. An entry appears at most once per section calculation.

### Ordering and line format

- unresolved sections: oldest unresolved driver group first, chronological within driver
- completed: chronological action/completion order
- stable Driver Name, Driver Code, entry ID tie-breakers

Preferred line format:

`270139 — Jamie Example [ABC123]: Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Requested.`

Without Unit snapshot:

`Jamie Example [ABC123]: Waiting on updated ETA.`

Whitespace is collapsed for concise operational lines.

### Editor isolation and session preservation

- first Handoff entry in a session generates from saved work
- Regenerate intentionally replaces editor text
- editing never mutates work/BOL/idle/report/settings
- Copy to Clipboard copies current edited text
- navigating away/back in the same session preserves edited draft
- Handoff is not continuously regenerated while typing

## Back/navigation and report refresh

Task Back returns to the actual prior Driver Workspace. Fleet search and selected Driver Code survive round-trip navigation. Alt+Left is available outside text-editing controls.

If `Update Reports` runs while navigated, WAA rebuilds current route through durable Driver Code/item/work-entry IDs. New Work/BOL note drafts remain in session. A stale entity shows an Unavailable route with a safe return path rather than a stale-reference crash.

## Theme, performance, and privacy

All current work/Handoff surfaces use dynamic Light/Dark resources. Ordinary text inherits `TextBrush`; semantic color supplements word status. DataGrid-generated text and editors explicitly follow current theme resources.

- no recurring timer/watcher
- no Excel/Office process
- no DB call on text keystrokes
- no per-row BOL/history queries
- driver work/BOL loads only for selected driver/state refresh
- task detail loads only when opened
- database/source parsing runs off UI thread through bounded operations
- Handoff generation only on first session entry or Regenerate
- queue virtualization remains enabled

Tests/fixtures use synthetic names, codes, leaders, units, orders, routes, customers, paths, notes, workbooks, and databases. Never commit production CSV/XLSX, databases, logs, or screenshots.

## Validation coverage

The Windows suite covers work migration/preservation, idle linkage, manual lifecycle, BOL task/action synchronization, queue aggregate counts/order/search, local-day activity/Handoff, deduplication, editor isolation, failed-save retention, duplicate-submit prevention, navigation/back/state restoration, report refresh, theme/source audit, contrast, keyboard route contracts, WPF/XAML compilation, and self-contained Windows x64 publishing.
