# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-handoff tool. It must make the current fleet easy to work, prevent missed follow-up, preserve historical context, and generate an accurate handoff without making the user reread every driver.

The driver is the center of the model. Driver Code is durable identity; Unit Code and Driver Leader are observed context that may change without splitting history.

The application must remain calm, compact, professional, and responsive on a low-end Windows office PC.

## Non-negotiable product rules

1. The current queue is the home screen; no dashboard blocks useful work.
2. Driver identity is never derived from truck or leader assignment.
3. Ordinary unresolved work carries forward until explicitly resolved.
4. Idle accountability is keyed by Driver Code + Report Cycle Date.
5. An idle action automatically creates one linked work entry in the same transaction.
6. Handoff is generated from work entries and is editable without mutating history.
7. Reports update once at launch and thereafter only through `Update Reports`.
8. A failed import or database migration never silently wipes the last known-good state.
9. Status uses words first and semantic color second.
10. The repository contains synthetic fixtures only.
11. Current source is the only implementation authority unless the user explicitly requests history.

## Runtime and performance boundary

- .NET 8 WPF desktop application
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA`
- no installer or administrator requirement
- one process; no WebView, browser UI, local HTTP server, Node, cloud service, or helper process
- no `FileSystemWatcher`, recurring report scan, polling timer, or continuous animation
- virtualized fleet rows
- indexed selected-driver, unresolved-count, local-day handoff, and idle-link queries
- one aggregate unresolved-count query as part of fleet loading, never one query per row
- selected-driver work loads on selection/state changes, not on every keystroke
- handoff generates only when entering the view or pressing Regenerate
- database writes remain short and transactional

## Implemented milestones

### Phase 1 — Roster, reports, and weighted idle: implemented

Delivered and validated:

- Windows Downloads known-folder discovery
- newest valid `rolling 7 day_data*.csv` selection
- launch-only automatic report update
- explicit manual `Update Reports`
- stable read, SHA-256 idempotency, validation, and atomic import
- Driver Code + Driver Name parsing
- Unit Code and Driver Leader context
- repeated `OOR %` / `Idle %` normalization
- SQLite persistence and last-known-good roster protection
- weighted driver/fleet 7-day and complete-coverage 28-day calculations
- configurable strict-greater-than threshold
- compact searchable virtualized fleet list
- light and dark appearance persistence
- portable Windows x64 workflow artifact

### Phase 2 — Current-cycle idle accountability: implemented

Delivered and validated:

- `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up`
- immutable metric/threshold/unit/leader/source snapshots
- same-cycle report correction preservation
- natural new-cycle rollover without deleting history
- unfinished high-idle priority ordering
- atomic event persistence

### Phase 3 — Driver work log: implemented in v0.2

Delivered and validated:

- `work_entries` migration without database recreation
- manual `Done`, `Waiting`, and `FollowUp`
- UTC creation and resolution timestamps
- unresolved carry-forward across restarts and report cycles
- Resolve without erasing original status/text/time
- Reopen while preserving history
- Unit Code, Driver Leader, and report-cycle snapshots
- one linked work entry for each idle contact
- idempotent legacy idle-event backfill
- per-driver Open Work and Today’s Activity
- fleet aggregate Open Work counts
- four-band queue priority incorporating ordinary unresolved work
- search-respecting `Next Needing Attention`

### Phase 4 — Deterministic handoff: implemented in v0.2

Delivered and validated:

- same-window Handoff view
- `Back to Queue`, `Regenerate`, and `Copy to Clipboard`
- editable multiline draft
- deterministic `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` sections
- local-calendar-day grouping from UTC timestamps
- linked idle activity displayed once through unified work history
- snapshot Unit Code context in generated lines
- section counts
- edits isolated from repository state

## Current acceptance result

The v0.2 implementation has passed the Windows workflow’s restore, warnings-as-errors build/XAML compilation, core tests, app/integration tests, self-contained win-x64 publish, and artifact upload. The final documentation commit must also pass that same complete workflow before the milestone artifact is released.

## Future phases — explicitly out of v0.2

### Phase 5 — Missing BOL

Potential future work:

- ingest an approved Missing BOL source
- attach BOL work to Driver Code while preserving shipment/document context
- surface unresolved document work in the same queue and handoff

This phase requires a fresh source contract and a separate bounded execution prompt.

### Phase 6 — Maintenance evaluation

Maintenance data and workflow are not implemented. Evaluate separately before adding schema or UI.

### Phase 7 — DOT evaluation

DOT data and workflow are not implemented. Evaluate separately before adding schema or UI.

## Deferred refinements

The following are not completion blockers for v0.2 and must not be implied as implemented:

- destructive work deletion
- full idle-event correction/audit UI
- keyboard-first shortcut pass
- separate Driver Leader filter control
- representative low-end hardware benchmark outside GitHub-hosted Windows validation
- Missing BOL, maintenance, or DOT integrations

## Failure behavior

- invalid/locked report: reject and retain last known-good roster
- missing 28-day period: show incomplete coverage
- zero denominator: show `N/A`
- work save failure: keep typed text for retry and report the error
- linked idle-work failure: roll back both records
- migration failure: log and show the actual startup error; never create a fresh database in its place
- clipboard failure: preserve the editor and report the error
- concurrent operation: disable duplicate submission while the write is active
