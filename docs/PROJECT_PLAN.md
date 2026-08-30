# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-handoff tool. It must make the current fleet easy to work, prevent missed follow-up, preserve historical context, and generate an accurate handoff without making the user reread every driver.

The driver is the center of the model. Driver Code is durable identity; Unit Code and Driver Leader are observed context that may change without splitting history.

The application must remain calm, compact, professional, readable in Light and Dark modes, and responsive on a low-end Windows office PC.

## Non-negotiable product rules

1. Fleet Queue is the fresh-launch home; no dashboard blocks useful work.
2. One `MainWindow` is the operations shell; driver/task/Handoff/unmatched work uses one central content host.
3. Driver identity and driver-owned routes use durable Driver Code, never truck, leader, or name similarity.
4. Ordinary unresolved work carries forward until explicitly resolved.
5. Idle accountability is keyed by Driver Code + Report Cycle Date.
6. An idle action automatically creates one linked work entry in the same transaction.
7. A matched unresolved Missing BOL item owns at most one linked task.
8. Missing BOL matches only exact normalized source Driver Code to exact durable Driver Code.
9. Handoff is generated from saved work and is editable without mutating history.
10. Reports update once at launch and thereafter only through `Update Reports`.
11. A failed source import or database migration never silently wipes last-known-good state.
12. Ordinary text inherits the active Light/Dark theme; fixed UI text colors outside palette dictionaries are prohibited.
13. Status uses words first and semantic color second.
14. The repository contains synthetic fixtures only.
15. Current source is the only implementation authority unless the user explicitly requests history.

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
- report watchers/background polling
- decorative animation, blur, glow, or gamification

Missing BOL remains a small exact-code imported work queue integrated with the central driver/task workflow, work log, and Handoff.

## Runtime and performance boundary

- .NET 8 native WPF desktop application
- one top-level `MainWindow`
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA`
- no installer or administrator requirement
- one process; no Excel process, Office automation, WebView, browser UI, local HTTP server, Node, cloud service, or helper process
- no `FileSystemWatcher`, recurring report scan, polling timer, or continuous animation
- one central `ContentControl` route host, no hidden legacy split pane
- virtualized/recycling fleet rows
- indexed aggregate fleet/unresolved/BOL reads, never one query per queue row
- selected-driver work/BOL loads only for selection/state/route refresh, not on every keystroke
- one focused task detail loaded only when opened
- report/database operations kept off the UI thread where they can block
- Handoff generation on first session entry or explicit Regenerate, not every navigation return/edit
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
- searchable virtualized fleet list
- persisted Light/Dark preference
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
- unresolved carry-forward across restarts/report cycles
- Resolve without erasing original status/text/time
- Reopen while preserving history
- Unit Code, Driver Leader, and report-cycle snapshots
- one linked work entry per idle contact
- idempotent legacy idle-event backfill
- per-driver Open Work and Today’s Activity
- aggregate fleet Open Work counts
- four-band queue priority incorporating unresolved work
- search-respecting `Next Needing Attention`

### Phase 4 — Deterministic Handoff: implemented in v0.2

Delivered and validated:

- same-window Handoff workflow
- `Back to Queue`, `Regenerate`, and `Copy to Clipboard`
- editable multiline draft
- deterministic `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` sections
- local-calendar-day grouping from UTC timestamps
- linked activity displayed once through unified work history
- snapshot Unit Code context
- section counts
- edits isolated from repository state

### Phase 5 — Missing BOL work queue: implemented in v0.3

Delivered and Windows-validated:

- managed local read-only `Order Details Missing BOL*.xlsx` ingestion without Excel/Office/COM
- first qualifying worksheet selection instead of hard-coded sheet name
- safe header normalization and duplicate irrelevant `Terminal Leader` tolerance
- shared-string, inline-string, numeric, formatted-identifier, text-date, and Excel-serial-date support
- complete in-memory validation before atomic mutation
- SHA-256 idempotency and independent Rolling/BOL update outcomes
- non-destructive BOL imports/items/actions/work-links schema
- exact normalized `Last Dispatch Driver cd` → durable Driver Code matching only
- leading-zero preservation and no numeric driver-code conversion
- visible unmatched rows without name/unit/leader/truck/fuzzy guessing
- later exact roster attachment with task creation exactly once
- one linked unresolved Missing BOL task per matched unresolved item
- Requested, Attempted, Follow-up, Resolved, and Reopen
- atomic item/task/action/activity writes and append-only history
- disappearance without automatic resolution
- resolved-present-again warning without automatic reopen
- source driver reassignment conflicts rejected without moving work history
- BOL fleet count and deterministic Order # search
- Missing BOL ordinary-work priority and deterministic Handoff integration without duplication

### Phase 6 — Central Workspace + Theme-Safe Text v0.4: implementation complete, final validation pending

The v0.4 branch implements the bounded presentation/navigation milestone without changing the v0.3 database schema or business/data rules.

Implemented central workspace work:

- one persistent `MainWindow` shell and one central `ContentControl`
- full-width Fleet Queue replacing the old all-at-once split pane
- single-click/Enter row opening of Driver Workspace by durable Driver Code
- focused `DriverWorkspace`, `IdleTask`, `MissingBolTask`, `WorkItemTask`, `NewWork`, `ActivityDetail`, `Handoff`, `UnmatchedBol`, and graceful `Unavailable` routes
- real in-session back stack and breadcrumbs
- safe `Alt+Left` Back that does not hijack TextBox/PasswordBox editing
- Driver Workspace as a compact work index rather than a page containing every editor
- deduplicated idle/BOL linked work representation
- explicit Add Work, Next Work Item, Next Needing Attention, BOL-focus, and Open Work-focus actions
- New Work success returns coherently to same Driver Workspace and retains new-item context
- read-only Activity Detail and Unmatched BOL routes
- Handoff edited-draft preservation across navigation; Regenerate remains explicit replacement
- session preservation for queue search/selection and unsaved per-driver/per-BOL notes
- stable-ID route restoration after report refresh and graceful stale-entity handling
- deterministic within-driver Next Work ordering before reuse of existing search-respecting fleet priority

Implemented theme work:

- `Themes/LightColors.xaml` and `Themes/DarkColors.xaml` as literal-color owners
- `Themes/BaseStyles.xaml` as dynamic resource consumer
- `ThemeManager` swaps only the active palette dictionary
- implicit theme-safe ordinary text across current text-bearing WPF controls
- dedicated secondary/disabled/primary/selected/link/semantic/focus resources
- explicit DataGridTextColumn display/edit element styles to prevent system-black text
- live Light/Dark switching without restart or route reset
- title-bar update on supported Windows builds
- async local theme-preference persistence with visible rollback on save failure
- deterministic palette-key/source audit tests
- deterministic WCAG-style contrast tests for normal text, semantic text, selected rows, controls, hover states, and UI boundaries

This phase is marked **implemented** only after the final branch workflow, documentation update, merge, and final `main` Windows workflow are successful. Until then, this section deliberately records validation as pending.

## Validated baseline before v0.4

WAA Missing BOL v0.3 passed the complete Windows workflow on August 30, 2026:

- restore
- warnings-as-errors Release build
- WPF/XAML compilation
- 24 core tests
- 65 app/SQLite/queue/view-model/integration tests
- 89 tests total, zero failures/skips
- self-contained Windows x64 publish
- portable artifact upload

v0.4 preserves those tests and adds navigation/theme/source-audit/contrast regression coverage. The exact final v0.4 count is recorded in `docs/IMPLEMENTATION_STATUS.md` only after the final workflow passes.

## Future phases

### Phase 7 — Maintenance evaluation

Maintenance data/workflow are not implemented. Evaluate separately before adding schema or UI.

### Phase 8 — DOT evaluation

DOT data/workflow are not implemented. Evaluate separately before adding schema or UI.

## Deferred refinements

The following are not completion blockers for v0.4 and must not be implied as implemented:

- destructive work deletion
- full corrective idle-event editing/audit UI
- dedicated Driver Leader filter control
- representative low-end office-PC benchmark outside GitHub-hosted Windows validation
- manual association of unmatched BOL rows; exact Driver Code existence remains required
- maintenance workflow
- DOT workflow

Keyboard access for current Fleet/Driver/Task navigation is part of v0.4 rather than deferred.

## Failure behavior

- invalid/locked Rolling report: reject and retain last-known-good roster
- invalid/locked/conflicting BOL workbook: reject and retain last accepted BOL state
- one report source failing: preserve/report the independent outcome of the other source
- missing BOL workbook: preserve known BOL source-presence flags/local work state
- source disappearance: mark absent from latest source but never resolve
- source driver reassignment conflict: reject snapshot and preserve prior item/task/history
- missing 28-day period: show incomplete coverage
- zero denominator: show `N/A`
- manual/BOL save failure: retain typed text/note for retry
- linked idle-work failure: roll back both records
- BOL action failure: roll back item, task, action event, and activity entry together
- migration failure: log/show actual startup error; never create a fresh database behind the user
- clipboard failure: preserve editor and report error
- theme preference save failure: restore prior visible theme and report error
- route entity disappears after refresh: show a focused Unavailable state with Back path
- no next visible work: keep context and state clearly that no other visible drivers currently need attention
- concurrent operation: disable duplicate submission while the write is active
