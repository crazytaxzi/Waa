# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-handoff tool. It must make the current fleet easy to work, prevent missed follow-up, preserve historical context, and generate an accurate handoff without making the user reread every driver.

The driver is the center of the model. Driver Code is durable identity; Unit Code and Driver Leader are observed context that may change without splitting history.

The application must remain calm, compact, professional, and responsive on a low-end Windows office PC.

## Non-negotiable product rules

1. The current queue is the home screen; no dashboard blocks useful work.
2. Driver identity is never derived from truck, leader, or name similarity.
3. Ordinary unresolved work carries forward until explicitly resolved.
4. Idle accountability is keyed by Driver Code + Report Cycle Date.
5. An idle action automatically creates one linked work entry in the same transaction.
6. A matched unresolved Missing BOL item owns at most one linked task.
7. Missing BOL matches only exact normalized source Driver Code to exact durable Driver Code.
8. Handoff is generated from work entries and is editable without mutating history.
9. Reports update once at launch and thereafter only through `Update Reports`.
10. A failed source import or database migration never silently wipes the last-known-good state.
11. Status uses words first and semantic color second.
12. The repository contains synthetic fixtures only.
13. Current source is the only implementation authority unless the user explicitly requests history.

## Permanent product exclusions

WAA is not and will not become a communications, document-management, OCR, analytics-portal, financial-dashboard, matching, or escalation platform.

Permanently excluded unless the user explicitly reverses the decision:

- emailing or transmitting documents
- automatic calling, messaging, or driver contact
- OCR or document-image recognition
- document upload, storage, attachment, or document-management workflows
- giant Missing BOL dashboards or separate BOL analytics portals
- revenue or financial summaries from the BOL workbook
- fuzzy identity matching, fuzzy record linking, name similarity, or probabilistic merges
- truck-, Unit Code-, or Driver Leader-based driver identity
- complex escalation trees, routing engines, approval workflows, or multi-level escalation logic
- browser dashboards, WebView, local web servers, Node, cloud services, or helper processes

Missing BOL remains a small exact-code imported work queue integrated with the existing driver queue, work log, and handoff.

## Runtime and performance boundary

- .NET 8 WPF desktop application
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA`
- no installer or administrator requirement
- one process; no Excel process, Office automation, WebView, browser UI, local HTTP server, Node, cloud service, or helper process
- no `FileSystemWatcher`, recurring report scan, polling timer, or continuous animation
- virtualized fleet rows
- indexed selected-driver, unresolved-count, BOL-count, local-day handoff, and source-link queries
- aggregate unresolved work and BOL counts, never one query per fleet row
- selected-driver work/BOL loads on selection or saved-state changes, not on every keystroke
- report parsing off the UI thread
- handoff generation only when entering the view or pressing Regenerate
- short transactional database writes

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
- four-band queue priority incorporating unresolved work
- search-respecting `Next Needing Attention`

### Phase 4 — Deterministic handoff: implemented in v0.2

Delivered and validated:

- same-window Handoff view
- `Back to Queue`, `Regenerate`, and `Copy to Clipboard`
- editable multiline draft
- deterministic `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` sections
- local-calendar-day grouping from UTC timestamps
- linked activity displayed once through unified work history
- snapshot Unit Code context in generated lines
- section counts
- edits isolated from repository state

### Phase 5 — Missing BOL work queue: implemented in v0.3

Delivered and Windows-validated:

- managed, local, read-only `Order Details Missing BOL*.xlsx` ingestion without Excel/Office/COM
- first qualifying worksheet selection rather than hard-coded sheet name
- safe header normalization and tolerance for duplicate irrelevant `Terminal Leader` columns
- shared-string, inline-string, numeric, formatted-identifier, text-date, and Excel-serial-date support
- complete in-memory validation before atomic mutation
- SHA-256 idempotency and independent Rolling/BOL update outcomes
- separate non-destructive SQLite schema for BOL imports, items, actions, and work links
- exact normalized `Last Dispatch Driver cd` to durable Driver Code matching only
- leading-zero preservation and no numeric driver-code conversion
- visible unmatched rows without name, unit, leader, truck, or fuzzy guessing
- later exact roster attachment with task creation exactly once
- one linked unresolved Missing BOL task per matched unresolved item
- Requested, Attempted, Follow-up, Resolved, and Reopen actions
- atomic item/task/action/activity writes and append-only action history
- disappearance from a later workbook without automatic resolution
- resolved-present-again warning without automatic reopen
- source-driver reassignment conflicts rejected without moving work history
- compact BOL fleet count, unmatched summary/list, and Order # search
- multiple selected-driver BOL cards with direct actions and retained failed-save notes
- Missing BOL participation as ordinary unresolved work without displacing unfinished high-idle work
- deterministic Needs Follow-up and Completed Today handoff integration without duplication
- complete light/dark dynamic-resource coverage

## Current acceptance result

WAA Missing BOL v0.3 passed the complete Windows workflow on August 30, 2026:

- restore
- warnings-as-errors Release build
- WPF/XAML compilation
- 24 core tests
- 65 app/SQLite/queue/view-model/integration tests
- 89 tests total, zero failures and zero skips
- self-contained Windows x64 publish
- portable artifact upload

The release documentation is part of the same source tree and must continue to pass the complete workflow before the final artifact is delivered.

## Future phases

### Phase 6 — Maintenance evaluation

Maintenance data and workflow are not implemented. Evaluate separately before adding schema or UI.

### Phase 7 — DOT evaluation

DOT data and workflow are not implemented. Evaluate separately before adding schema or UI.

## Deferred refinements

The following are not completion blockers for v0.3 and must not be implied as implemented:

- destructive work deletion
- full idle-event correction/audit UI
- keyboard-first shortcut pass
- separate Driver Leader filter control
- representative low-end hardware benchmark outside GitHub-hosted Windows validation
- manual association of unmatched BOL rows; exact Driver Code existence remains required
- maintenance workflow
- DOT workflow

## Failure behavior

- invalid/locked Rolling report: reject and retain last-known-good roster
- invalid/locked/conflicting BOL workbook: reject and retain last accepted BOL state
- one report source failing: preserve and report the independent outcome of the other source
- missing BOL workbook: preserve all known BOL source-presence flags and local work state
- source disappearance: mark absent from latest source but never resolve
- source driver reassignment conflict: reject snapshot and preserve prior item/task/history
- missing 28-day period: show incomplete coverage
- zero denominator: show `N/A`
- work save failure: keep typed text for retry and report the error
- linked idle-work failure: roll back both records
- BOL action failure: roll back item, task, action event, and activity entry together
- migration failure: log and show the actual startup error; never create a fresh database in its place
- clipboard failure: preserve the editor and report the error
- concurrent operation: disable duplicate submission while the write is active
