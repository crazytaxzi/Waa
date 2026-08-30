# WAA Implementation Status

## Current bounded milestone

**WAA Work Log + Handoff v0.2 — implemented and Windows-validated.**

## Runtime and deployment

- .NET 8 WPF desktop application
- warnings treated as errors
- Windows x64 self-contained portable publish
- no installer, administrator requirement, SDK, or separately installed .NET runtime for the published build
- one local desktop process
- SQLite database and preferences under `%LOCALAPPDATA%\WAA`
- GitHub Actions workflow performs restore, build, tests, portable publish, and artifact upload

## Implemented

### Reports and roster

- resolves the current Windows Downloads known folder
- accepts `rolling 7 day_data*.csv`
- performs one automatic update during launch
- performs later updates only through `Update Reports`
- no watcher, periodic polling, or automatic mid-session refresh
- stable read, SHA-256 idempotency, validation, and atomic import
- failed reports retain the last known-good roster
- Driver Code durable identity
- Driver Name display identity
- Unit Code associated-truck context
- Driver Leader organizational context with ten-character round-trip support

### Idle calculations and accountability

- repeated source-row normalization
- weighted driver/fleet 7-day idle
- weighted driver/fleet 28-day idle with complete four-period coverage rules
- zero-denominator `N/A` and incomplete-coverage presentation
- configurable threshold, default 50%, strict greater-than comparison
- current-cycle `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up`
- same-cycle contact preservation and new-cycle rollover
- metric, threshold, unit, leader, source, and timestamp snapshots

### Driver work log

- non-destructive `work_entries` schema migration and indexed queries
- manual `Done`, `Waiting`, and `FollowUp`
- Done resolves immediately
- Waiting and FollowUp carry across restart until resolved
- Resolve preserves original status, text, creation time, and snapshots
- Reopen clears only the resolution timestamp
- automatic linked work for idle contact, saved atomically
- idempotent backfill for idle events created by the pre-work-log build
- per-driver Open Work and Today’s Activity
- fleet Open Work aggregate counts without per-row queries
- direct actions disabled for blank work text and while saving
- failed manual saves retain typed text for retry

### Queue and navigation

- compact searchable virtualized fleet list remains visible while working a driver
- four priority bands combining unfinished high-idle attention and ordinary unresolved work
- stable idle concern/name/code tie-breakers
- threshold changes rerank without changing history
- `Next Needing Attention` prefers unfinished high-idle contact work, then ordinary unresolved work
- active search is respected; hidden drivers are not selected

### Handoff

- Handoff is the only secondary top-level view and remains in the main window
- deterministic service independent of WPF launch
- editable handoff draft
- explicit `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY`
- local calendar-day boundaries derived from the PC time zone while storing UTC
- unresolved oldest-first grouped predictably by driver
- completed chronological for the current local day
- linked idle activity appears once through unified work history
- `Regenerate` intentionally replaces the editor
- `Copy to Clipboard` copies current user edits
- editing/copying never mutates repository records

### Appearance and privacy

- persisted light and dark modes
- dynamic theme resources for new surfaces and semantic status text
- no real employee identities or production reports in fixtures

## Validation

The milestone suite contains:

- 9 core parser/math tests
- 35 app, SQLite migration, repository, queue, handoff, view-model, theme, and integration tests
- 44 tests total

The Windows workflow compiles WPF/XAML and publishes the complete self-contained portable folder after tests pass.

## Not implemented

- Missing BOL ingestion/workflow
- maintenance workflow
- DOT workflow
- destructive work-entry deletion
- full corrective idle-event editing/audit UI
- dedicated keyboard-shortcut pass
- separate Driver Leader filter control
- measured benchmark on the user’s representative low-end office PC

Those items remain future bounded milestones. They are not partially hidden in v0.2.
