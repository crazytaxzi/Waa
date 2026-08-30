# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-handoff tool. It must make the current fleet easy to work, prevent missed follow-up, preserve historical context, and generate an accurate handoff without making the user reread every driver.

Driver Code is durable identity; Unit Code and Driver Leader are observed context that may change without splitting history. The application remains calm, compact, professional, readable in Light and Dark modes, and responsive on a low-end Windows office PC.

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
11. Failed imports/migrations never silently wipe last-known-good state.
12. Ordinary text inherits active Light/Dark theme; fixed UI text colors outside palette dictionaries are prohibited.
13. Status uses words first and semantic color second.
14. Repository fixtures are synthetic only.
15. Current source is the only implementation authority unless history is explicitly requested.

## Permanent product exclusions

Permanently excluded unless explicitly reversed:

- emailing/transmitting documents
- automatic calling, messaging, or driver contact
- OCR/image recognition
- document upload/storage/attachment management
- giant Missing BOL dashboards or separate BOL analytics/financial portals
- fuzzy/name/unit/truck/leader/probabilistic matching
- truck-, Unit Code-, or Driver Leader-based identity
- complex escalation trees, routing engines, approval workflows
- browser/WebView/local HTTP server/Node/cloud/helper processes
- report watchers/background polling
- decorative animation, blur, glow, or gamification

Missing BOL remains a small exact-code imported work queue integrated with the central driver/task workflow, work log, and Handoff.

## Runtime and performance boundary

- .NET 8 native WPF
- one top-level `MainWindow`
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA`
- no installer/admin requirement
- one local desktop process
- no Excel/Office/COM/browser/local-server/Node/cloud/helper process
- no FileSystemWatcher/recurring scan/polling timer
- one central `ContentControl`; no hidden legacy split pane
- virtualized/recycling fleet rows
- indexed aggregate fleet/unresolved/BOL reads, never one query per queue row
- selected-driver work/BOL loads only for selection/state/route refresh, not keystrokes
- focused task detail loads only when opened
- database/report work runs off UI thread where it can block
- Handoff generates on first session entry or explicit Regenerate, not every navigation return/edit
- short transactional writes

## Implemented milestones

### Phase 1 — Roster, reports, weighted idle: implemented

Validated Rolling 7 Day ingestion, durable Driver Code identity, Unit/Leader context, SHA-256/idempotent atomic import, weighted driver/fleet 7-day and complete-coverage 28-day calculations, configurable strict-greater-than threshold, virtualized searchable queue, persisted appearance, and portable Windows publishing.

### Phase 2 — Current-cycle idle accountability: implemented

Validated Not Contacted / Attempted / Spoke / Spoke — Follow-up, immutable metric/threshold/unit/leader/source snapshots, same-cycle correction preservation, new-cycle rollover, unfinished high-idle priority, and atomic event persistence.

### Phase 3 — Driver work log v0.2: implemented

Validated Done / Waiting / FollowUp, UTC creation/resolution timestamps, unresolved carry-forward, Resolve/Reopen history preservation, context snapshots, linked idle work, legacy backfill, per-driver Open Work/Today’s Activity, fleet aggregate counts, queue integration, and search-respecting Next Needing Attention.

### Phase 4 — Deterministic Handoff v0.2: implemented

Validated editable deterministic Needs Follow-up / Waiting-Pending / Completed Today output, local-day grouping from UTC, linked activity deduplication, snapshot context, Regenerate, and Copy to Clipboard with editor isolation.

### Phase 5 — Missing BOL v0.3: implemented

Validated managed read-only XLSX ingestion, worksheet/header normalization, supported cell/date formats, atomic source snapshots, exact Driver Code matching only, unmatched preservation/later exact attachment, one linked task, Requested/Attempted/Follow-up/Resolved/Reopen, append-only actions, item/task/action/activity atomicity, source-disappearance semantics, reassignment-conflict rejection, queue/search/Handoff integration, and no fuzzy matching.

### Phase 6 — Central Workspace + Theme-Safe Text v0.4: implemented and Windows-validated

Central workspace delivered:

- persistent one-window shell and central `ContentControl`
- full-width Fleet Queue replacing old split pane
- single-click/Enter Driver Workspace navigation by durable Driver Code
- focused DriverWorkspace / IdleTask / MissingBolTask / WorkItemTask / NewWork / ActivityDetail / Handoff / UnmatchedBol / Unavailable routes
- real back stack, breadcrumbs, safe Alt+Left
- Driver Workspace as compact work index
- deduplicated idle/BOL linked-work presentation
- Add Work, Next Work Item, Next Needing Attention, BOL/Open Work focus actions
- New Work success returns to same driver with saved-item context
- read-only Activity Detail and Unmatched BOL
- Handoff draft preservation across navigation
- queue search/selection and unsaved New Work/BOL-note preservation
- stable-ID route restoration after report refresh and graceful stale-item handling
- deterministic within-driver work ordering before reuse of existing fleet priority

Theme-safe text delivered:

- centralized `LightColors.xaml` / `DarkColors.xaml` literal palettes
- `BaseStyles.xaml` dynamic resource consumers
- ThemeManager swaps only active color dictionary
- implicit theme-safe ordinary text across current WPF controls
- dedicated subtle/disabled/primary/selected/link/semantic/focus resources
- explicit DataGridTextColumn display/edit theme foreground handling
- live Light/Dark switching without restart/navigation reset
- title-bar theme update where supported
- async local appearance persistence with visible rollback on save failure
- repository-wide fixed-color source audit
- deterministic contrast tests for normal/semantic/selected/control/editor/hover/focus combinations

Validation on PR branch run **#47** (`33335165695`) on August 30, 2026:

- restore succeeded
- warnings-as-errors Release build and WPF/XAML compilation succeeded
- 24 core tests passed
- 165 app/SQLite/navigation/theme/source-audit/integration tests passed
- **189 tests total, 0 failed, 0 skipped**
- **0 build warnings, 0 build errors**
- self-contained win-x64 publish succeeded
- portable artifact upload succeeded

The exact documentation/release tree is still required to pass after this validation-record commit, and merged `main` must pass once more before release delivery. Those are release gates, not additional product work.

## Regression baseline

v0.3 entered this milestone with 89 validated tests (24 core + 65 app). v0.4 preserves that behavior and expands coverage to 189 tests through navigation, state-preservation, theme/source audit, contrast, keyboard, stale-route, and hover regressions.

No v0.4 database migration is required.

## Future phases

### Phase 7 — Maintenance evaluation

Maintenance data/workflow are not implemented. Evaluate separately before adding schema or UI.

### Phase 8 — DOT evaluation

DOT data/workflow are not implemented. Evaluate separately before adding schema or UI.

## Deferred refinements

Not v0.4 completion blockers:

- destructive work deletion
- full corrective idle-event editing/audit UI
- dedicated Driver Leader filter
- representative low-end office-PC benchmark outside GitHub-hosted validation
- manual association of unmatched BOL; exact Driver Code remains required
- maintenance workflow
- DOT workflow

Keyboard access for the current Fleet/Driver/Task flow is part of v0.4, not deferred.

## Failure behavior

- invalid/locked Rolling report: reject and retain last-known-good roster
- invalid/locked/conflicting BOL workbook: reject and retain last accepted BOL state
- one report source failing: preserve/report independent outcome of the other
- missing BOL workbook: preserve known BOL presence/local work state
- source disappearance: mark absent, never resolve automatically
- source reassignment conflict: reject and preserve prior item/task/history
- missing 28-day period: show incomplete coverage
- zero denominator: `N/A`
- manual/BOL save failure: retain typed text/note
- linked idle failure: roll back event/work
- BOL action failure: roll back item/task/action/activity together
- migration failure: surface actual error; never replace database
- clipboard failure: preserve editor and report error
- appearance-save failure: restore prior visible theme and report error
- stale route after refresh: show Unavailable with safe Back path
- no next visible work: retain context and report it clearly
- concurrent operation: block duplicate submission
