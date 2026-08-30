# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-Handoff tool. It must make the current fleet easy to work, prevent missed follow-up, preserve historical context, and generate an accurate handoff without forcing the user to reread every driver.

Driver Code is durable identity. Unit Code and Driver Leader are context that may change without splitting history. The application remains compact, professional, readable in Light/Dark modes, and responsive on a low-end Windows office PC.

## Non-negotiable product rules

1. Fleet Queue is fresh-launch home.
2. One `MainWindow` is the operations shell; driver/task/Handoff/unmatched work uses one central content host.
3. Driver identity and routes use durable Driver Code, never truck, leader, or name similarity.
4. Ordinary unresolved work carries forward until explicitly resolved.
5. Idle accountability is keyed by Driver Code + Report Cycle Date.
6. An idle action creates exactly one linked work entry in the same transaction.
7. A matched unresolved Missing BOL item owns at most one linked task.
8. Missing BOL matches only exact normalized source Driver Code to exact durable Driver Code.
9. Handoff is generated from saved work, remains editable, and editing/copying never mutates saved history.
10. Reports update once at launch and thereafter only through explicit `Update Reports`.
11. Failed imports/migrations never silently wipe last-known-good state.
12. Ordinary text inherits the active Light/Dark theme; fixed UI text colors outside palettes are prohibited.
13. Status uses words first and semantic color second.
14. Repository fixtures are synthetic only.
15. Current source/docs are implementation authority unless history is explicitly requested.

## Permanent exclusions

Unless explicitly reversed, WAA does not add or prepare architecture for:

- emailing/transmitting documents
- automatic calling, messaging, or driver contact
- OCR/image recognition
- document upload/storage/attachment management
- giant Missing BOL dashboards or BOL analytics/financial portals
- fuzzy/name/unit/truck/leader/probabilistic identity matching
- complex escalation trees, routing engines, or approval workflows
- browser/WebView/local HTTP server/Node/cloud/helper processes
- report watchers/background polling
- decorative animation, blur, glow, or gamification

Missing BOL remains an exact-code local work workflow inside the central driver/task flow, work log, and Handoff.

## Runtime/performance boundary

- .NET 8 native WPF
- one top-level `MainWindow`
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA`
- no installer/admin requirement
- one local desktop process
- no Excel/Office/COM/browser/local-server/Node/cloud/helper process
- no FileSystemWatcher/recurring scan/polling timer
- one central `ContentControl`; no hidden legacy split pane
- virtualized/recycling Fleet rows
- indexed aggregate fleet/unresolved/BOL reads, never one query per row
- selected-driver state loads only for selection/state/route refresh, not keystrokes
- focused task detail loads only when opened
- database/report work runs off UI thread where blocking is possible
- Handoff generates only on first session entry or explicit Regenerate
- short transactional writes

## Implemented milestones

### Phase 1 — Roster, reports, weighted idle

Validated Rolling 7 Day ingestion, durable Driver Code identity, Unit/Leader context, SHA-256/idempotent atomic import, weighted driver/fleet 7-day and complete-coverage 28-day calculations, configurable threshold, searchable virtualized fleet, persisted appearance, and portable Windows publishing.

### Phase 2 — Current-cycle idle accountability

Validated Not Contacted / Attempted / Spoke / Spoke — Follow-up, saved metric/threshold/unit/leader/source context, same-cycle preservation, new-cycle rollover, unfinished high-idle priority, and atomic event persistence.

### Phase 3 — Driver work log v0.2

Validated Done / Waiting / FollowUp, UTC creation/resolution timestamps, unresolved carry-forward, Resolve/Reopen history preservation, context snapshots, linked idle work, legacy backfill, per-driver Open Work/Today’s Activity, aggregate fleet counts, queue integration, and search-respecting Next Needing Attention.

### Phase 4 — Deterministic editable Handoff v0.2

Established saved-work Handoff generation, local-day grouping, linked-activity deduplication, Regenerate, Copy to Clipboard, and editor isolation. The visible presentation was later compacted in v0.4.2 without changing the saved work model.

### Phase 5 — Missing BOL v0.3

Validated managed read-only XLSX ingestion, worksheet/header normalization, supported cell/date formats, atomic source snapshots, exact Driver Code matching only, unmatched preservation/later exact attachment, one linked task, Requested/Attempted/Follow-up/Resolved/Reopen, append-only actions, synchronized item/task/action/activity writes, source-disappearance behavior, reassignment-conflict rejection, queue/search/Handoff integration, and no fuzzy matching.

### Phase 6 — Central Workspace + Theme-Safe Text v0.4

Delivered:

- persistent one-window shell and central content host
- full-width Fleet Queue replacing the split pane
- single-click/Enter Driver Workspace navigation by durable Driver Code
- focused DriverWorkspace / IdleTask / MissingBolTask / WorkItemTask / NewWork / ActivityDetail / Handoff / UnmatchedBol / Unavailable routes
- real Back stack, breadcrumbs, and safe Alt+Left
- Driver Workspace as compact work index
- deduplicated idle/BOL linked-work presentation
- Add Work, Next Work Item, Next Needing Attention, BOL/Open Work focus actions
- New Work success returning to same driver
- read-only Activity Detail and Unmatched BOL
- Handoff draft preservation across navigation
- queue search/selection and unsaved New Work/BOL note preservation
- stable-ID route restoration after report refresh and graceful stale-item handling
- centralized Light/Dark palettes and dynamic base styles
- DataGrid generated-text theme handling
- live Light/Dark switching and local preference persistence
- fixed-color source audit and deterministic contrast tests

Validated v0.4 branch baseline: 24 core + 165 app tests = 189 total, zero failures/skips and zero build warnings/errors.

### Phase 6.1 — v0.4.1 startup binding hotfix

A real portable-startup failure exposed a WPF `Run.Text` binding default that XAML compilation did not catch. v0.4.1:

- makes every data-bound inline `Run.Text` explicitly one-way for display
- adds repository-wide regression coverage for that rule
- preserves database/business behavior

Validated v0.4.1: 24 core + 166 app tests = 190 total, zero failures/skips and zero build warnings/errors.

### Phase 6.2 — v0.4.2 compact driver-grouped Handoff

Changes only the generated Handoff presentation:

- opening editable convention: `No open ACE/ACI's`
- WAA does **not** model or validate ACE/ACI state; user edits the opening when untrue
- one alphabetical narrative line per driver rather than visible state-section duplication
- current fleet Unit/Driver Name preferred for handoff identity
- idle Handoff prose keeps concise action + human note instead of repeating 28D/7D metric boilerplate
- Missing BOL action narrative prefers human note when available
- dedicated `Missing BOLs:` section
- one line per driver with all unresolved order numbers grouped together
- singular/plural `order` / `orders`
- no Empty Call Date, route, or local BOL status repeated in the copied BOL section
- old visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` headings removed from runtime draft
- underlying deterministic work/local-day classification remains intact and regression-tested
- no schema migration, priority change, source-rule change, or BOL-state change

Code-format validation on PR #4 run #53 passed 24 core + 167 app tests = **191 total**, zero failures/skips, zero build warnings/errors, with successful win-x64 publish. Final documented-tree and merged-main validation remain release gates.

## Regression baseline

The v0.3 product entered central-workspace work with 89 validated tests. v0.4.x preserves those data/business rules and expands coverage for navigation, theme/source audits, contrast, keyboard/back behavior, runtime binding safety, stale routes, compact Handoff grouping, current-unit preference, and BOL aggregation.

No v0.4.x database schema migration is required.

## Future phases

### Maintenance evaluation

Maintenance data/workflow are not implemented. Evaluate separately before adding schema or UI.

### DOT evaluation

DOT data/workflow are not implemented. Evaluate separately before adding schema or UI.

## Deferred refinements

- destructive work deletion
- full corrective idle-event editing/audit UI
- dedicated Driver Leader filter
- representative low-end office-PC benchmark outside GitHub-hosted validation
- manual association of unmatched BOL; exact Driver Code remains required
- maintenance workflow
- DOT workflow

## Failure behavior

- invalid/locked Rolling report: reject and retain last-known-good roster
- invalid/locked/conflicting BOL workbook: reject and retain last accepted BOL state
- one source failing: preserve/report independent result of the other
- missing BOL workbook: preserve known BOL/local work state
- source disappearance: mark absent, never resolve automatically
- source reassignment conflict: reject and preserve prior item/task/history
- missing 28-day period: show incomplete coverage
- zero denominator: show `N/A`
- manual/BOL save failure: retain typed text/note
- linked idle failure: roll back event/work
- BOL action failure: roll back item/task/action/activity together
- migration failure: surface actual error; never replace database
- clipboard failure: preserve editor and report error
- appearance-save failure: restore prior visible theme and report error
- stale route after refresh: show Unavailable with safe Back path
- no next visible work: retain context and report clearly
- concurrent operation: block duplicate submission
