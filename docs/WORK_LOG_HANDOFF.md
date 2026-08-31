# WAA Work Log + Handoff v0.4.4 Presentation / v0.3 Data Contract

This document is authoritative for persistent driver work history and deterministic shift Handoff, including idle linkage and Missing BOL task/action integration. The v0.4.x presentation uses the central one-window workspace while preserving the validated v0.3 work/history/database rules.

## Driver-centric ownership

- Every work entry belongs to durable Driver Code.
- Driver Name is display identity.
- Unit Code and Driver Leader are context/snapshots, never driver identity.
- Later roster assignments never move, duplicate, or rewrite historical work.
- Missing BOL source names/leaders never replace durable WAA identity/context.
- Driver/task routes use Driver Code and persisted record IDs rather than Unit Code.

## SQLite work model

`work_entries` stores:

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

Allowed statuses are `Done`, `Waiting`, and `FollowUp`.

Base persisted sources remain `Manual` and `IdleContact`. Missing BOL semantic sources are overlaid through `missing_bol_work_links` as `MissingBolTask` and `MissingBolAction`.

Semantics:

- Manual Done resolves at creation.
- Waiting/FollowUp remain unresolved until explicit Resolve.
- `resolved_utc` is authoritative; resolution never erases original status, text, creation time, or context snapshots.
- Reopen clears only `resolved_utc` for supported ordinary Waiting/FollowUp work.
- MissingBolTask remains unresolved until synchronized BOL Resolve.
- MissingBolAction is completed activity at action time.
- timestamps are stored in UTC and displayed using the PC time zone.
- work text is trimmed and may not be blank.
- work entries are not destructively deleted.

The current central-workspace, compact-Handoff, Driver Leader grouping, and theme presentation changes require no database migration and do not increment schema version.

## Idle contact integration

Idle event and linked work entry save in one transaction.

| Idle outcome | Work status | Resolution |
|---|---|---|
| Spoke | Done | event timestamp |
| Attempted | FollowUp | unresolved |
| Spoke — Follow-up | FollowUp | unresolved |

Persisted idle work text keeps saved 28-day/7-day metric snapshots and optional note. Driver Workspace represents the current actionable idle state once rather than duplicating its linked work as a manual item.

## Missing BOL task integration

Every newly matched unresolved Missing BOL item without a task creates exactly one linked FollowUp work entry in the same import transaction.

Example persisted task text:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots matched Driver Code, Unit Code, Driver Leader, report cycle when available, creation UTC, item linkage, and source import. Reimport/Reopen reuse the same task. Source/status wording may update while original creation/context snapshots remain intact. Source disappearance never resolves or deletes the task.

Driver Workspace represents each unresolved BOL item once as a Missing BOL attention row. It does not duplicate MissingBolTask as manual work. Generic Work Item Resolve/Reopen remains unavailable for MissingBolTask, and database guards reject bypass state changes.

## Missing BOL action integration

Requested, Attempted, Follow-up, Resolved, and Reopen each append an action event and create one completed activity work entry.

| Action | Item state | Task state |
|---|---|---|
| Requested | Requested | unresolved |
| Attempted | Attempted | unresolved |
| Follow-up | FollowUp | unresolved |
| Resolved | Resolved + timestamp | resolved same timestamp |
| Reopen | Open, resolution cleared | same task reopened |

Optional notes are retained in action history and appended to the saved activity. Item/task/action/activity writes remain atomic. Duplicate submit is blocked while a save is active and failed saves retain typed notes.

## Driver Workspace work index

Fleet Queue opens a full Driver Workspace in the same MainWindow. Driver Workspace shows summary context, `NEEDS ATTENTION`, Quick Actions, and compact Today’s Activity.

Actionable work is represented once:

1. unfinished idle contact
2. each unresolved Missing BOL item
3. each unresolved manual Waiting/FollowUp item

Each actionable row opens a focused task workspace rather than exposing all editors at once.

### Manual Work Item

Shows Driver identity, original status/text/time/source, Unit/Leader/report-cycle snapshots, resolution state/time, and Resolve/Reopen where supported. MissingBolTask cannot use generic resolution.

### New Work

`Add Work` opens one multiline editor with Done, Waiting, and Follow-up.

- blank/whitespace input cannot save
- input is trimmed
- duplicate submission is disabled while saving
- successful save refreshes fleet/driver work and returns to the same Driver Workspace
- failed save retains typed text
- per-driver draft survives in-session navigation
- Unit/Leader/report-cycle snapshots come from current driver context

### Today’s Activity

Today’s Activity uses PC local calendar-day boundaries and includes manual work created today, linked idle work created today, older ordinary work resolved today, and MissingBolAction activity created today. MissingBolTask itself is not rendered as completed activity.

Activity Detail is read-only and adds no edit/delete path.

## Fleet integration and Next Work Item

Fleet rows expose aggregate Open Work and unresolved matched BOL counts without one query per driver. Order # remains available for deterministic search.

Queue priority remains:

1. above-threshold unfinished idle contact
2. above-threshold Spoke with unresolved work before clear
3. remaining unresolved work including Missing BOL
4. clear fleet

`Next Work Item` orders one driver’s actionable work:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual FollowUp, oldest first
4. manual Waiting, oldest first
5. other supported unresolved manual work

When no next item remains for that driver, existing search-respecting `Next Needing Attention` is reused. No competing fleet-priority engine exists.

## Handoff workspace

Handoff is a focused full-width route in the same MainWindow. Controls remain:

- Back to Queue
- Regenerate
- editable multiline draft
- Copy to Clipboard

First Handoff entry in a session generates from saved work. Navigating away/back preserves the edited draft. Regenerate intentionally replaces it from current saved records. Editing/copying never mutates work, BOL, idle, reports, settings, or identity.

## v0.4.4 compact Driver Leader-grouped Handoff format

The visible generated draft is deliberately closer to an operational human handoff than a database report.

### Opening line

The draft begins with:

`No open ACE/ACI's`

This is a **user-requested editable handoff convention**. WAA does not currently model or validate ACE/ACI state. If the statement is not true for the shift, the user must edit it before copying the handoff.

### Driver Leader grouping

After the opening line, WAA separates narrative drivers under deterministic Driver Leader headings:

`Driver Leader: LEADER-A`

Only leaders with at least one driver represented in the generated Handoff appear. Driver Leader headings are ordered alphabetically. Within each Driver Leader, driver lines are alphabetical by Driver Name, then Driver Code.

WAA prefers the driver’s **current fleet Driver Leader** when the Driver Code is present in the current fleet. For historical/off-roster work where current leader context is unavailable, WAA falls back to the most useful saved `driver_leader_snapshot`. Blank or `*` leader values do not become headings; if no meaningful current or historical leader exists, the driver is grouped under `Driver Leader: Unassigned`.

Driver Leader remains organizational context only. Grouping never changes durable Driver Code ownership or rewrites historical snapshots.

### Driver narrative lines

Within each Driver Leader section, WAA emits at most one narrative line per driver.

Preferred identity:

`261535 — Andrew Example [A00001]: ...`

WAA prefers the driver’s current fleet Unit Code and current Driver Name when available. If current Unit is unavailable, it falls back to a useful saved work snapshot. Blank or `*` Unit values are omitted instead of being printed as a fake unit.

The narrative combines relevant saved work for that driver into one line:

- unresolved ordinary Waiting/FollowUp work
- unresolved linked idle-contact work
- ordinary work completed/resolved during the current local day
- linked idle activity completed during the current local day
- MissingBolAction activity completed during the current local day

MissingBolTask itself is excluded from the narrative because unresolved BOL orders are rendered in the dedicated Missing BOL section.

For idle activity, handoff prose removes the generated `28D / 7D` metric boilerplate and retains a concise action phrase plus the human-entered note. The metrics remain preserved in the underlying saved work/event records and task workspace; they are simply not repeated in the copied shift handoff.

For MissingBolAction activity, a human-entered note is preferred as the narrative text. When no note exists, the concise saved action text is retained so a meaningful completed action is not silently discarded.

Duplicate work-entry IDs and duplicate identical narrative phrases are collapsed.

### Missing BOL section

The draft then contains:

`Missing BOLs:`

Unresolved Missing BOL drivers are separated under the same `Driver Leader: ...` heading convention used by the narrative. Each driver appears once within its leader section. All unresolved MissingBolTask orders for that driver are grouped onto the same line.

The Missing BOL section uses the same leader precedence as the narrative: current fleet Driver Leader first, then historical work snapshot fallback, then `Unassigned` only when neither is meaningful.

Singular example:

`260811 — Allen Example [A00001]: Missing BOL for order AST3962`

Plural example:

`242163 — Brad Example [B00002]: Missing BOL for orders AST2543, ASU1575`

Within a driver, orders use the Empty Call Date already embedded in the deterministic MissingBolTask text for oldest-first ordering, then Order # as a stable tie-breaker. Duplicate order numbers collapse.

The compact section intentionally does **not** repeat:

- Empty Call Date
- origin/destination route
- local BOL status
- one separate line per order

Those details remain available in the focused Missing BOL workspace. Handoff only needs leader separation, driver identity, and the order list.

If no unresolved matched BOL tasks exist, the section displays `None.`.

### Removed visible headings

The runtime draft does not display:

- `NEEDS FOLLOW-UP`
- `WAITING / PENDING`
- `COMPLETED TODAY`

The underlying open/resolved/local-day classification remains deterministic and is still regression-tested; the visible handoff is grouped by Driver Leader and then driver instead of by database-state section.

## Back/navigation and report refresh

Task Back returns to the actual prior Driver Workspace. Fleet search and selected Driver Code survive round-trip navigation. Alt+Left is available outside text-editing controls.

If `Update Reports` runs while navigated, WAA rebuilds the current route using durable Driver Code/item/work IDs. New Work/BOL note drafts remain in session. A stale entity shows an Unavailable route with a safe return path.

## Theme, performance, and privacy

All work/Handoff surfaces use dynamic Light/Dark resources. Ordinary text inherits the active theme and semantic color supplements word status.

- no recurring timer/watcher
- no Excel/Office process
- no database call on text keystrokes
- no per-row history/BOL queries
- task detail loads only when opened
- database/source work stays in bounded operations off the UI thread where applicable
- Handoff generation runs only on first session entry or explicit Regenerate
- queue virtualization remains enabled

Tests and fixtures use synthetic identities/data only. Never commit production reports, databases, logs, or screenshots containing operational employee/customer data.

## Validation coverage

The Windows suite covers work migration/preservation, idle linkage, manual lifecycle, BOL task/action synchronization, queue aggregate counts/order/search, local-day activity, legacy classification regression, compact Driver Leader-grouped Handoff formatting, current-leader precedence, historical leader fallback, within-leader driver ordering, current-unit preference, BOL order aggregation, removal of BOL route/status boilerplate, editor isolation, failed-save retention, navigation/state restoration, theme/source audit, contrast, WPF/XAML compilation, and self-contained Windows x64 publishing.
