# WAA — Driver Work Queue and Shift Handoff

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, and producing an editable end-of-shift handoff from the saved history.

The application is driver-centric: **Driver Code** is the durable key, **Driver Name** is display identity, and Unit Code and Driver Leader are historical context rather than identity.

## Current release: Work Log + Handoff v0.2

WAA currently provides:

- a searchable, virtualized current-driver fleet queue
- weighted driver and fleet 7-day idle
- weighted driver and fleet 28-day idle with coverage
- configurable idle threshold, default 50%
- automatically prioritized current-cycle idle accountability
- `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up` presentation
- one automatic report update during launch and explicit `Update Reports` afterward
- manual work saved as `Done`, `Waiting`, or `Follow-up`
- unresolved Waiting and Follow-up items carried forward until resolved
- direct Resolve and Reopen actions without deleting history
- automatic idle-contact entries in the same unified work history
- per-driver Open Work and Today’s Activity
- an Open Work count in the fleet row
- `Next Needing Attention`, respecting the active search results
- deterministic editable Handoff generation
- `Copy to Clipboard` for the current edited handoff text
- persisted light and dark appearance
- local SQLite storage under `%LOCALAPPDATA%\WAA`
- self-contained Windows x64 portable publishing with no installer or administrator requirement

## Daily workflow

1. Launch `WAA.exe`. WAA immediately opens the saved database, then checks Downloads once for the newest valid `rolling 7 day_data*.csv` report.
2. Select a driver from the queue.
3. Record the current-cycle idle outcome when applicable. The idle event and its linked work entry save atomically, so the conversation is never typed twice.
4. Enter ordinary work and choose `Done`, `Waiting`, or `Follow-up`.
5. Resolve open items when completed. Their original text, status, creation time, and snapshots remain intact.
6. Choose `Next Needing Attention` to advance through visible drivers who still need idle contact or have unresolved ordinary work.
7. Open `Handoff`, edit the generated text as needed, then choose `Copy to Clipboard`.

Handoff edits are deliberately temporary. They never edit work history or resolve anything. `Regenerate` intentionally replaces the editor from current saved work.

## Queue priority

The automatic queue uses four bands:

1. Above-threshold drivers with `Not Contacted`, `Attempted`, or `Spoke — Follow-up`.
2. Above-threshold drivers with `Spoke`; those with unresolved ordinary work come first within this band.
3. Remaining drivers with unresolved work.
4. Remaining clear drivers.

Within unfinished high-idle work, `Spoke — Follow-up` comes first, then `Attempted`, then `Not Contacted`, followed by the highest current valid idle concern and stable driver tie-breakers.

Changing the threshold reranks immediately and does not rewrite saved contact or work history.

## Handoff output

The generated draft always contains:

- `NEEDS FOLLOW-UP`
- `WAITING / PENDING`
- `COMPLETED TODAY`

Unresolved sections are oldest-first and grouped predictably by driver. Completed Today is chronological for the PC’s current local calendar day. Timestamps are stored in UTC; WAA does not hard-code a time zone.

Linked idle activity appears once through its unified work entry. Unit Code snapshots are preferred because they preserve the context from when the work occurred.

## Report refresh behavior

WAA updates reports in exactly two ways:

1. once automatically during application launch
2. when the user explicitly chooses `Update Reports`

There is no folder watcher, recurring scan, polling timer, or automatic mid-session import. A bad, incomplete, or locked candidate never replaces the last known-good roster. Runtime report files remain read-only.

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
3. Double-click `WAA.exe`.

Upgrade:

1. Close WAA.
2. Extract the new portable folder.
3. Replace the old application folder with the new one.
4. Start `WAA.exe`.

The database and preferences remain under `%LOCALAPPDATA%\WAA`; replacing the portable application folder does not delete them. Database migrations are non-destructive and fail visibly rather than silently replacing an existing database.

## Privacy

This repository is public. Source and tests use synthetic identities only. Never commit real driver names, driver codes, leader codes, unit assignments, company reports, copied production databases, or logs containing employee information.

## Technical shape

- .NET 8
- native WPF
- `Microsoft.Data.Sqlite`
- one desktop process
- local persistence only
- indexed aggregate fleet/work reads
- short transactional writes
- no browser stack, local server, Node, cloud service, helper process, report watcher, or recurring timer

## Documentation

- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — source contracts and weighted calculations
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md) — current-cycle idle accountability and queue ordering
- [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md) — authoritative work-log, migration, and handoff specification
- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — implemented milestones and future boundaries
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — exact current implementation state
