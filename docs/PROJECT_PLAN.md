# WAA Project Plan

## Product goal

WAA is a personal driver-support work-through and shift-Handoff tool. It must make the current fleet easy to work, prevent missed saved follow-up, preserve historical work context, show the current Missing BOL report without inventing a second workflow, and generate an accurate handoff without forcing the user to reread every driver.

Driver Code is durable identity. Unit Code and Driver Leader are context that may change without splitting history. The application remains compact, professional, readable in Light/Dark modes, and responsive on a low-end Windows office PC.

## Non-negotiable product rules

1. Fleet Queue is fresh-launch home.
2. One `MainWindow` is the operations shell; driver/task/Handoff/unmatched work uses one central content host.
3. Driver identity and routes use durable Driver Code, never truck, leader, or name similarity.
4. Ordinary saved unresolved work carries forward until explicitly resolved.
5. Idle accountability is keyed by Driver Code + Report Cycle Date.
6. An idle action creates exactly one linked saved work entry in the same transaction.
7. Missing BOL is a read-only current-workbook view and does not own a persisted WAA work/status/action lifecycle.
8. Missing BOL matches only exact normalized source Driver Code to exact **current** durable Driver Code.
9. Handoff is generated from saved non-BOL work plus a transient projection of the current Missing BOL workbook; editing/copying never mutates saved/source state.
10. Reports scan once at launch and thereafter only through explicit `Update Reports`.
11. Failed Rolling imports/migrations never silently wipe last-known-good saved state; invalid Missing BOL files never restore invented DB BOL state.
12. Ordinary text inherits the active Light/Dark theme; fixed UI text colors outside palettes are prohibited.
13. Status uses words first and semantic color second.
14. Repository fixtures are synthetic only.
15. Current source/docs are implementation authority unless history is explicitly requested.

## Permanent exclusions

Unless explicitly reversed, WAA does not add or prepare architecture for emailing/transmitting documents, automatic calling/messaging, OCR/image recognition, document upload/storage/attachment management, giant Missing BOL dashboards/financial analytics, fuzzy/name/unit/truck/leader/probabilistic identity matching, complex escalation/routing/approval engines, browser/WebView/local HTTP server/Node/cloud/helper processes, report watchers/background polling, or heavy decorative animation beyond the explicitly approved bounded ambient layer.

## Runtime/performance boundary

- .NET 8 native WPF
- one top-level `MainWindow`
- Windows x64 self-contained portable publish
- SQLite under `%LOCALAPPDATA%\WAA` for durable roster/work/idle/settings state
- current Missing BOL workbook snapshot held only in memory
- no installer/admin requirement
- one local desktop process
- no Excel/Office/COM/browser/local-server/Node/cloud/helper process
- no FileSystemWatcher/recurring scan/polling timer
- one central `ContentControl`; no hidden legacy split pane
- virtualized/recycling Fleet rows
- indexed aggregate saved-work reads, never one query per row
- one bounded current BOL in-memory snapshot, no per-row BOL DB queries
- selected-driver state loads only for selection/state/route refresh, not keystrokes
- focused detail loads only when opened
- database/report work runs off UI thread where blocking is possible
- Handoff generates only on first session entry or explicit Regenerate
- short transactional saved-state writes

## Implemented milestones

### Phase 1 — Roster, reports, weighted idle

Validated Rolling 7 Day ingestion, durable Driver Code identity, Unit/Leader context, SHA-256/idempotent atomic import, weighted driver/fleet 7-day and complete-coverage 28-day calculations, configurable threshold, searchable virtualized fleet, persisted appearance, and portable Windows publishing.

### Phase 2 — Current-cycle idle accountability

Validated Not Contacted / Attempted / Spoke / Spoke — Follow-up, saved metric/threshold/unit/leader/source context, same-cycle preservation, new-cycle rollover, unfinished high-idle priority, and atomic event persistence.

### Phase 3 — Driver work log

Validated Done / Waiting / FollowUp, UTC creation/resolution timestamps, unresolved carry-forward, Resolve/Reopen history preservation, context snapshots, linked idle work, legacy backfill, per-driver Open Work/Today’s Activity, aggregate fleet counts, queue integration, and search-respecting Next Needing Attention.

### Phase 4 — Deterministic editable Handoff

Established saved-work Handoff generation, local-day grouping, Regenerate, Copy to Clipboard, and editor isolation; later compacted and grouped by Driver Leader.

### Phase 5 — Missing BOL parser/exact matching

Validated managed read-only XLSX parsing, worksheet/header normalization, supported cell/date forms, exact Driver Code matching, unmatched visibility, Order # validation, and no fuzzy matching.

The original v0.3–v0.4.5 persisted BOL task/action lifecycle has been superseded by v0.4.6 source-only behavior. Legacy DB artifacts are retained non-destructively on upgrades but are dormant.

### Phase 6 — Central Workspace + Theme-Safe Text

Delivered one-window Fleet → Driver → focused work/detail routing, real Back stack/breadcrumbs, state preservation, centralized Light/Dark resources, complete dark shell, virtualized Fleet Queue, compact Handoff, Driver Leader grouping, theme-safe generated text, contrast/source audits, and graceful stale routes.

### Phase 6.1–6.5 — presentation/runtime refinements

- v0.4.1 safe one-way inline `Run.Text` binding hotfix
- v0.4.2 compact one-line-per-driver Handoff + dedicated Missing BOL section
- v0.4.3 denser Fleet Queue + gunmetal/neon-purple/neon-green stream palette
- v0.4.4 Driver Leader-grouped Handoff + complete dark-shell background fix
- v0.4.5 bounded ambient scanline/electric-blue motes + subtle button motion
- v0.4.5.1 user-authoritative Ambient Motion toggle hotfix

### Phase 6.6 — v0.4.6 Source-Only Missing BOL

Current Missing BOL behavior is intentionally simpler:

- current accepted workbook is the source of truth
- parsed rows/hash live in memory only for the current session
- no current `missing_bol_*` DB tables on fresh installs
- no current BOL task/status/action/note/history writes
- exact match against current durable Driver Code only
- unmatched current rows remain visible/read-only
- driver has separate `CURRENT MISSING BOL` report section
- BOL presence does not increase Open Work/priority or enter `Next Work Item`/Today’s Activity
- read-only BOL detail replaces the old action editor
- Handoff Missing BOL section is regenerated transiently from current workbook rows
- old v0.3–v0.4.5 BOL tables/generated work remain physically untouched on upgrade but are classified/excluded from current work/priority/Handoff

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
- maintenance workflow
- DOT workflow

Manual/fuzzy association of unmatched BOL is not a deferred refinement; it is explicitly excluded. Exact current Driver Code remains required.

## Failure behavior

- invalid/locked Rolling report: reject and retain last-known-good saved roster
- missing Missing BOL workbook: clear current in-memory BOL view
- invalid/conflicting BOL candidates with no valid fallback: report failure and show no invented/restored BOL DB state
- newer invalid BOL candidate with older valid candidate: use the valid candidate and report the ignored failure
- one source failing: preserve/report the independent result of the other
- current BOL row disappearance: remove from current view after next accepted scan; do not carry forward/resolve locally
- missing 28-day period: show incomplete coverage
- zero denominator: show `N/A`
- manual work save failure: retain typed text
- linked idle failure: roll back event/work
- migration failure: surface actual error; never replace database
- clipboard failure: preserve editor and report error
- appearance-save failure: restore prior visible theme and report error
- stale route after refresh: show Unavailable with safe Back path
- no next visible work: retain context and report clearly
- concurrent operation: block duplicate submission