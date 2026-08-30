# WAA Implementation Status

## Current bounded milestone

**WAA Missing BOL v0.3 — implemented and Windows-validated.**

## Runtime and deployment

- .NET 8 native WPF desktop application
- warnings treated as errors
- Windows x64 self-contained portable publish
- no installer, administrator requirement, SDK, separately installed .NET runtime, Excel, or Office required by the published build
- one local desktop process
- SQLite database and preferences under `%LOCALAPPDATA%\WAA`
- GitHub Actions performs restore, build, WPF/XAML compilation, tests, portable publish, and artifact upload

## Implemented

### Reports and roster

- resolves the current Windows Downloads known folder
- accepts `rolling 7 day_data*.csv`
- accepts `Order Details Missing BOL*.xlsx` and ignores `~$` lock files
- performs one automatic update during launch
- performs later updates only through `Update Reports`
- no watcher, timer, polling, periodic scan, or automatic mid-session refresh
- stable file reads and complete-file SHA-256 idempotency
- independent Rolling 7 Day and Missing BOL outcomes with honest partial-update messaging
- failure preserves the last-known-good state for the affected source without rolling back the other source
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

### Missing BOL workbook parsing

- managed, local, read-only ZIP/XML XLSX reader; no Excel/Office/COM process
- first qualifying worksheet selection without hard-coded sheet name
- BOM/non-breaking-space/outer/internal whitespace header normalization
- duplicate irrelevant `Terminal Leader` tolerance
- ambiguous required-header rejection
- shared strings, inline strings, ordinary/numeric/blank cells
- full numeric identifier rendering without scientific notation
- zero-padded identifier preservation when workbook formatting supplies it
- text Empty Call Date and Excel serial-date support
- `Order #` exact durable source-item identity
- identical duplicate-row collapse and conflicting duplicate rejection
- required-field/cell-specific validation before any mutation
- irrelevant fields, including Total Revenue, ignored

### Exact matching and source lifecycle

- exact trimmed uppercase-invariant source Driver Code to durable Driver Code matching only
- identifiers stored/compared as text with leading zeros preserved
- matches all durable driver entities, including historical/non-current records
- source name retained as evidence and never used as identity
- exact-code name mismatch warning without overwriting durable Driver Name
- blank/unknown source codes retained visibly as unmatched
- no name, Unit Code, truck, leader, prefix, substring, fuzzy, similarity, or probabilistic matching
- later exact roster code attaches a previously unmatched item and creates its task once
- atomic accepted-workbook snapshots with separate import/item/action/link tables
- missing workbook does not mark known items absent
- disappearance from a later accepted workbook marks source absence but never resolves local work
- resolved item present again remains resolved and is visibly flagged
- source driver-code reassignment conflict rejects the snapshot without moving work/history

### Missing BOL tasks and actions

- one linked unresolved FollowUp task per matched unresolved item
- reimport and Reopen reuse the same task
- task snapshots Driver Code, Unit Code, Driver Leader, report cycle, source import, and creation UTC
- Requested, Attempted, Follow-up, Resolved, and Reopen
- append-only action history
- one completed activity work entry per action
- atomic item/task/action/activity transactions
- failed action keeps typed note text for retry
- duplicate submission disabled while an item action saves
- general Open Work cannot bypass BOL item state; task resolution/reopen is intentionally directed through the BOL controls

### Driver work log

- non-destructive `work_entries` migration and indexed queries
- manual Done, Waiting, and FollowUp
- Done resolves immediately
- Waiting and FollowUp carry across restart until resolved
- Resolve preserves original status, text, creation time, and snapshots
- Reopen clears only resolution timestamp
- automatic linked work for idle contact, saved atomically
- idempotent legacy idle-event backfill
- effective sources for Manual, IdleContact, MissingBolTask, and MissingBolAction
- per-driver Open Work and Today’s Activity
- fleet Open Work aggregate counts without per-row queries
- failed manual saves retain typed text for retry

### Queue, search, and selected-driver workflow

- compact searchable virtualized fleet list remains visible while working a driver
- BOL column shows unresolved matched Missing BOL subset
- Open Work continues to show all unresolved work, including BOL tasks
- aggregate indexed BOL counts, oldest dates, unmatched count, and Order # search text
- compact header summary and same-window unmatched list
- four priority bands preserve unfinished high-idle work above ordinary BOL work
- older Empty Call Date tie-breaker within otherwise equal ordinary unresolved work
- threshold changes rerank without changing history
- deterministic search includes attached Order # text without fuzzy matching
- `Next Needing Attention` includes drivers whose only open issue is Missing BOL and respects active search
- selected-driver Missing BOL section supports multiple orders, bounded scrolling, source/name/presence warnings, notes, and direct actions

### Handoff

- Handoff remains the only secondary top-level view in the main window
- deterministic service independent of WPF launch
- editable draft with Needs Follow-up, Waiting / Pending, and Completed Today
- local calendar-day boundaries derived from PC time zone while storing UTC
- unresolved MissingBolTask appears once in Needs Follow-up
- MissingBolAction created today appears once in Completed Today
- resolved BOL task is excluded from Completed Today so its Resolved action is the one completion line
- Reopen returns the same task to Needs Follow-up and appends Reopened activity
- snapshot Unit Code, Driver Name/Code, Order #, date, route, and status carried in operational lines
- Regenerate intentionally replaces the editor
- Copy to Clipboard copies current user edits
- editing/copying never mutates repository records

### Appearance, privacy, and exclusions

- persisted light and dark modes
- dynamic theme resources for BOL cards, unmatched list, warnings, inputs, buttons, borders, and text
- source uses words plus restrained semantic color; no animation, glow, decorative gradient, or dashboard tile
- no real employee, customer, order, route, or production workbook data in fixtures
- permanent exclusions remain absent: email/transmission, automatic contact, OCR, document storage/uploads/attachments, analytics/financial portals, fuzzy matching, escalation/routing/approval engines, browser/WebView/local server/Node/cloud/helper processes

## Validation

Final v0.3 suite:

- 24 core parser/math/XLSX tests
- 65 app, SQLite migration, repository, report-update, queue, selected-driver, work-log, handoff, view-model, theme, and integration tests
- 89 tests total
- 0 failures
- 0 skipped
- 0 build warnings

The successful Windows workflow compiled WPF/XAML and published/uploaded the complete self-contained portable Windows x64 folder.

## Compatibility result

- existing v0.2 database, threshold, appearance preference, roster, observations, idle contacts, and work history are preserved
- legacy idle events are still backfilled idempotently
- Missing BOL schema is added non-destructively and advances the database schema version
- migration failure surfaces the actual error and leaves existing data/schema intact; no replacement database is created
- replacing the portable application folder does not remove `%LOCALAPPDATA%\WAA`

## Not implemented

- manual/fuzzy assignment of unmatched BOL items; exact durable Driver Code remains required
- emailing/transmitting BOLs or documents
- automatic calls/messages/contact
- OCR, image recognition, document uploads/storage/attachments
- BOL analytics, financial/revenue summaries, dashboards, escalation/routing/approval workflows
- maintenance workflow
- DOT workflow
- destructive work-entry deletion
- full corrective idle-event editing/audit UI
- dedicated keyboard-shortcut pass
- separate Driver Leader filter control
- measured benchmark on the user’s representative low-end office PC

These are not partially hidden in v0.3. Maintenance and DOT remain separate future evaluations; permanently excluded capabilities remain permanently excluded unless the user explicitly reverses that decision.
