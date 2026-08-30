# WAA — Driver Work Queue and Shift Handoff

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, reviewing Missing BOL orders, and producing an editable end-of-shift handoff from saved history.

The application is driver-centric: **Driver Code** is the durable key, **Driver Name** is display identity, and Unit Code and Driver Leader are historical context rather than identity.

## Current release: Missing BOL v0.3

WAA currently provides:

- a searchable, virtualized current-driver fleet queue
- weighted driver and fleet 7-day idle
- weighted driver and fleet 28-day idle with coverage
- configurable idle threshold, default 50%
- automatically prioritized current-cycle idle accountability
- `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up` presentation
- one automatic report update during launch and explicit `Update Reports` afterward
- managed, read-only `Order Details Missing BOL*.xlsx` ingestion without Excel or Office
- exact Driver Code matching with unmatched source codes preserved visibly
- a compact unresolved BOL count in each fleet row and Order # search
- selected-driver Missing BOL review with Requested, Attempted, Follow-up, Resolved, and Reopen
- exactly one linked open task for each matched unresolved Missing BOL item
- unresolved Missing BOL work carried forward until explicitly resolved
- manual work saved as `Done`, `Waiting`, or `Follow-up`
- unresolved Waiting and Follow-up items carried forward until resolved
- direct Resolve and Reopen actions without deleting history
- automatic idle-contact and Missing BOL action entries in the unified work history
- per-driver Open Work and Today’s Activity
- an Open Work count in the fleet row
- `Next Needing Attention`, respecting active search results
- deterministic editable Handoff generation
- `Copy to Clipboard` for the current edited handoff text
- persisted light and dark appearance
- local SQLite storage under `%LOCALAPPDATA%\WAA`
- self-contained Windows x64 portable publishing with no installer or administrator requirement

## Daily workflow

1. Launch `WAA.exe`. WAA opens the saved database, then checks Downloads once for both `rolling 7 day_data*.csv` and `Order Details Missing BOL*.xlsx`.
2. Select a driver from the queue. The queue stays visible while the driver card shows idle context, Missing BOL, Open Work, New Work, and Today’s Activity.
3. Record the current-cycle idle outcome when applicable. The idle event and linked work entry save atomically.
4. Review the driver’s Missing BOL items. Record Requested, Attempted, Follow-up, or Resolved; use Reopen only when a resolved item needs work again.
5. Enter ordinary work and choose Done, Waiting, or Follow-up.
6. Resolve ordinary open items when completed. Their original text, status, creation time, and snapshots remain intact.
7. Choose `Next Needing Attention` to advance through visible drivers who still need idle contact or have unresolved work, including Missing BOL tasks.
8. Open `Handoff`, edit the generated text as needed, then choose `Copy to Clipboard`.

Handoff edits are temporary. They never edit work history, Missing BOL state, idle events, or reports. `Regenerate` intentionally replaces the editor from current saved work.

## Missing BOL source and matching

Expected workbook family:

`Order Details Missing BOL*.xlsx`

Temporary Office lock files beginning with `~$` are ignored. WAA reads XLSX locally through a bounded managed ZIP/XML parser; it does not require Excel, Office, COM automation, Internet access, or administrator rights.

WAA finds the first worksheet whose first non-empty row contains the required headers. Header matching trims a BOM, converts non-breaking spaces, trims outer whitespace, collapses repeated internal whitespace, and compares case-insensitively. Repeated irrelevant `Terminal Leader` headers are tolerated. Required duplicate headers are rejected as ambiguous.

`Order #` is the durable Missing BOL item key. `Last Dispatch Driver cd` is matched only by trimmed uppercase-invariant exact text to WAA Driver Code. Codes remain text, so meaningful leading zeros are preserved. `Last Dispatch Driver nm` is supporting evidence only and never replaces WAA’s durable Driver Name.

There is no name, unit, truck, leader, substring, prefix, similarity, or fuzzy matching. Blank or unknown exact codes remain in the compact Unmatched BOL list and create no driver-owned task. If a later Rolling 7 Day import introduces the exact Driver Code, WAA attaches the item and creates its task once.

## Missing BOL lifecycle

Each accepted workbook is an atomic source snapshot. WAA hashes the complete file with SHA-256, skips an already accepted hash, validates the workbook fully in memory, and then commits the snapshot.

For a matched new item, WAA creates one unresolved linked Follow-up task. Reimporting the same order never creates another task. Disappearance from a later workbook never resolves local work. A missing item remains open with a restrained `Not in latest report` indicator. A resolved item that appears again remains resolved, is marked `Resolved — present again in latest report`, and can be explicitly reopened.

Supported local states:

- Open
- Requested
- Attempted
- Follow-up
- Resolved

Reopen returns a resolved item to Open and reopens the same linked task. Every action appends history and creates one completed activity entry. Item state, task state, action event, and activity entry save together or all roll back.

## Queue priority and search

The automatic queue uses four bands:

1. Above-threshold drivers with Not Contacted, Attempted, or Spoke — Follow-up.
2. Above-threshold drivers with Spoke; those with unresolved work come first.
3. Remaining drivers with unresolved work, including Missing BOL tasks.
4. Remaining clear drivers.

Within unfinished high-idle work, Spoke — Follow-up comes first, then Attempted, then Not Contacted, followed by the highest current valid idle concern and stable tie-breakers. Missing BOL never buries unfinished high-idle accountability. Within otherwise equal ordinary unresolved work, an older open Empty Call Date may rank first.

Search matches Driver Code, Driver Name, Unit Code, Driver Leader, and attached Missing BOL Order # text. It is deterministic substring search, not fuzzy matching. `Next Needing Attention` examines only visible search results.

## Handoff output

The generated draft always contains:

- `NEEDS FOLLOW-UP`
- `WAITING / PENDING`
- `COMPLETED TODAY`

Unresolved Missing BOL tasks appear once in Needs Follow-up with snapshot Unit Code, Driver Name/Code, Order #, Empty Call Date, route, and current local state. Missing BOL actions recorded today appear once in Completed Today. Resolving a BOL item removes its task from Needs Follow-up; reopening returns the same task.

Unresolved sections are oldest-first and grouped predictably by driver. Completed Today is chronological for the PC’s current local calendar day. Timestamps are stored in UTC; WAA does not hard-code a time zone.

## Report refresh behavior

WAA updates reports in exactly two ways:

1. once automatically during application launch
2. when the user explicitly chooses `Update Reports`

There is no folder watcher, recurring scan, polling timer, or automatic mid-session import. Rolling 7 Day and Missing BOL import independently: one may succeed while the other fails, and the status message identifies both outcomes. A bad, incomplete, locked, older, or conflicting source never replaces its last-known-good state. Runtime reports remain read-only and are never moved, renamed, deleted, or rewritten.

## Weighted idle rules

- Driver 7-day = current idle hours / current engine hours × 100.
- Driver 28-day = summed idle hours / summed engine hours across the current period and three expected prior weekly periods.
- Fleet percentages are also weighted numerator/denominator calculations.
- A missing expected period displays incomplete 28-day coverage rather than an invented percentage.
- A zero denominator displays `N/A`.

WAA never averages weekly percentages to produce the 28-day value.

## Portable installation and upgrade

WAA targets **.NET 8 WPF** and is published self-contained for Windows x64.

First install:

1. Extract the complete `WAA-Portable-win-x64` ZIP to a normal local folder.
2. Do not run it from inside the ZIP.
3. Place current reports in the Windows Downloads folder when available.
4. Double-click `WAA.exe`.

Upgrade:

1. Close WAA.
2. Extract the new portable folder.
3. Replace the old application folder with the new one.
4. Keep `%LOCALAPPDATA%\WAA` in place.
5. Start `WAA.exe`.

The database and preferences remain under `%LOCALAPPDATA%\WAA`; replacing the portable application folder does not delete them. Database migrations are non-destructive and fail visibly rather than silently replacing an existing database.

## Permanent exclusions

WAA is deliberately not an email, messaging, calling, OCR, document storage, attachment management, analytics portal, financial dashboard, fuzzy-matching, escalation, routing, approval, browser, cloud, or document-management product. Missing BOL remains a compact local work queue integrated with the existing driver workflow.

## Privacy

This repository is public. Source and tests use synthetic identities and generated synthetic workbooks only. Never commit real driver names, driver codes, leader codes, unit assignments, order numbers, customer names, company reports, copied production databases, screenshots, or logs containing employee/company information.

## Technical shape

- .NET 8
- native WPF
- `Microsoft.Data.Sqlite`
- managed ZIP/XML XLSX reader
- one desktop process
- local persistence only
- indexed aggregate fleet/work/BOL reads
- short transactional writes
- no Excel process, Office automation, browser stack, local server, Node, cloud service, helper process, report watcher, or recurring timer

## Documentation

- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — source contracts, precedence, and weighted calculations
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md) — current-cycle idle accountability and queue ordering
- [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md) — authoritative Missing BOL workflow
- [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md) — authoritative work-log and handoff specification
- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — implemented milestones and future boundaries
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — exact current implementation state
