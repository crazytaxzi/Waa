# WAA Work Log + Handoff v0.4.6

This document is authoritative for persistent driver work history and deterministic shift Handoff. Saved work history covers manual work and idle-contact linkage. Missing BOL is current-report information only and is projected into Handoff transiently from the current workbook.

## Driver-centric ownership

- Every saved work entry belongs to durable Driver Code.
- Driver Name is display identity.
- Unit Code and Driver Leader are context/snapshots, never driver identity.
- Later roster assignments never move, duplicate, or rewrite historical saved work.
- Driver/task routes use Driver Code and persisted work IDs rather than Unit Code.
- Current Missing BOL rows are not saved work and never acquire durable WAA work ownership.

## SQLite work model

`work_entries` stores saved operational work:

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

Current persisted sources are `Manual` and `IdleContact`. Allowed statuses are `Done`, `Waiting`, and `FollowUp`.

Semantics:

- Manual Done resolves at creation.
- Waiting/FollowUp remain unresolved until explicit Resolve.
- `resolved_utc` is authoritative; resolution never erases original status, text, creation time, or context snapshots.
- Reopen clears only `resolved_utc` for supported ordinary Waiting/FollowUp work.
- timestamps are stored in UTC and displayed using the PC time zone.
- work text is trimmed and may not be blank.
- work entries are not destructively deleted.

Older upgraded databases may contain historical `MissingBolTask` / `MissingBolAction` work linked through legacy `missing_bol_work_links`. v0.4.6 leaves that data physically untouched for non-destructive compatibility but excludes it from current Open Work, Today’s Activity, queue priority, and current ordinary Handoff narrative.

## Idle contact integration

Idle event and linked work entry save in one transaction.

| Idle outcome | Work status | Resolution |
|---|---|---|
| Spoke | Done | event timestamp |
| Attempted | FollowUp | unresolved |
| Spoke — Follow-up | FollowUp | unresolved |

Persisted idle work text keeps saved 28-day/7-day metric snapshots and optional note. Driver Workspace represents the current actionable idle state once rather than duplicating its linked work as a manual item.

## Manual Work

`Add Work` opens one multiline editor with Done, Waiting, and Follow-up.

- blank/whitespace input cannot save
- input is trimmed
- duplicate submission is disabled while saving
- successful save refreshes fleet/driver work and returns to the same Driver Workspace
- failed save retains typed text
- per-driver draft survives in-session navigation
- Unit/Leader/report-cycle snapshots come from current driver context

The focused Work Item view shows original status/text/time/source, Unit/Leader/report-cycle snapshots, resolution state/time, and Resolve/Reopen where supported.

## Current Missing BOL is not work

v0.4.6 does not create or update work entries from `Order Details Missing BOL*.xlsx`.

Current matched Missing BOL rows:

- live only in memory from the current accepted workbook
- appear in Driver Workspace under `CURRENT MISSING BOL`
- may open a read-only order detail
- do not increase Open Work
- do not enter `NEEDS ATTENTION`
- do not enter `Next Work Item`
- do not create Today’s Activity
- do not have Requested/Attempted/Follow-up/Resolved/Reopen state, notes, or action history

If a row disappears from the workbook, it disappears from WAA after the next launch/manual report scan. There is no local BOL resolution/carry-forward lifecycle.

## Driver Workspace work index

Fleet Queue opens a full Driver Workspace in the same MainWindow.

`NEEDS ATTENTION` contains actionable saved work only:

1. unfinished idle contact
2. unresolved manual FollowUp
3. unresolved manual Waiting/other supported manual work

`CURRENT MISSING BOL` is a separate read-only report section and is not part of the actionable list.

### Today’s Activity

Today’s Activity uses PC local calendar-day boundaries and includes manual work created today, linked idle work created today, and older ordinary work resolved today. Current Missing BOL rows are not activity records.

Activity Detail is read-only and adds no edit/delete path.

## Fleet integration and Next Work Item

Fleet rows may display current Missing BOL counts and current Order # search text, but those report values are distinct from Open Work.

Queue priority remains:

1. above-threshold unfinished idle contact
2. above-threshold Spoke with unresolved saved work before clear
3. remaining unresolved saved work
4. clear fleet

Current Missing BOL presence alone does not elevate priority.

`Next Work Item` orders one driver’s actionable work:

1. unfinished idle contact
2. manual FollowUp, oldest first
3. manual Waiting, oldest first
4. other supported unresolved manual work

Current Missing BOL report rows are skipped. When no next item remains for that driver, existing search-respecting `Next Needing Attention` is reused.

## Handoff workspace

Handoff is a focused full-width route in the same MainWindow. Controls remain:

- Back to Queue
- Regenerate
- editable multiline draft
- Copy to Clipboard

First Handoff entry in a session generates from saved non-BOL work plus the current in-memory Missing BOL workbook view. Navigating away/back preserves the edited draft. Regenerate intentionally replaces it from current saved work/current BOL rows. Editing/copying never mutates work, BOL, idle, reports, settings, or identity.

## Compact Driver Leader-grouped Handoff format

The draft begins with:

`No open ACE/ACI's`

This is a **user-requested editable handoff convention**. WAA does not model or validate ACE/ACI state.

### Driver Leader grouping

Narrative drivers are separated under deterministic headings:

`Driver Leader: LEADER-A`

Headings are alphabetical. Drivers within each leader are alphabetical by Driver Name, then Driver Code.

For saved work, WAA prefers current fleet Driver Leader/Unit/Name when the Driver Code is current and uses useful saved snapshots as fallback. Blank or `*` Unit/Leader values are not presented as meaningful context.

### Driver narrative lines

WAA emits at most one ordinary narrative line per represented driver. It combines relevant saved non-BOL work for that driver:

- unresolved ordinary Waiting/FollowUp work
- unresolved linked idle-contact work
- ordinary work completed/resolved during the current local day
- linked idle activity completed during the current local day

Idle prose may remove generated 28D/7D metric boilerplate while retaining the saved human note. The underlying saved metrics/events are preserved in SQLite; only copied prose is compacted.

Legacy BOL task/action work from older WAA releases is excluded from current ordinary narrative.

Duplicate work-entry IDs and duplicate identical narrative phrases are collapsed.

### Missing BOL section

The draft then contains:

`Missing BOLs:`

This section is generated **transiently from current matched workbook rows**, not from saved BOL tasks/history.

Each represented driver appears once within its current Driver Leader group. Current workbook Order # values for that driver are grouped on one line.

Singular example:

`260811 — Allen Example [A00001]: Missing BOL for order AST3962`

Plural example:

`242163 — Brad Example [B00002]: Missing BOL for orders AST2543, ASU1575`

The compact section intentionally does not repeat Empty Call Date, route, or local BOL status. Local BOL status no longer exists in v0.4.6; those current-file details remain available in the read-only Missing BOL detail.

If no current matched BOL rows exist, the section displays `None.`.

### Removed visible headings

The runtime draft does not display `NEEDS FOLLOW-UP`, `WAITING / PENDING`, or `COMPLETED TODAY`. Underlying saved-work/local-day classification remains deterministic; visible handoff is grouped by Driver Leader and driver.

## Back/navigation and report refresh

Task/detail Back returns to the actual prior Driver Workspace. Fleet search and selected Driver Code survive round-trip navigation. Alt+Left is available outside text-editing controls.

If `Update Reports` runs while navigated, WAA rebuilds the current route using stable Driver Code/item/work IDs. New Work drafts remain in session. A Missing BOL order that no longer exists in the newly accepted workbook becomes unavailable rather than being restored from SQLite.

## Theme, performance, and privacy

All work/Handoff surfaces use dynamic Light/Dark resources.

- no recurring timer/watcher
- no Excel/Office process
- no database call on text keystrokes
- no per-row Missing BOL database queries
- current BOL detail derives from one bounded in-memory workbook snapshot
- database/source work stays in bounded operations off the UI thread where applicable
- Handoff generation runs only on first session entry or explicit Regenerate
- queue virtualization remains enabled

Tests and fixtures use synthetic identities/data only. Never commit production reports, databases, logs, or screenshots containing operational employee/customer data.

## Validation coverage

The Windows suite covers work migration/preservation, idle linkage, manual lifecycle, source-only BOL memory/restart/replacement behavior, exact-code current-roster matching, legacy BOL-work exclusion, current-file fleet/search/detail presentation, Next Work BOL exclusion, transient current-file Handoff projection, compact Driver Leader grouping, navigation/state restoration, theme/source audit, contrast, WPF/XAML compilation, and self-contained Windows x64 publishing.