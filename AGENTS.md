# WAA repository rules

WAA is a clean, driver-centric Windows work application. The current `main` tree, this file, the README, and the current documents under `docs/` are the only implementation authority unless the user explicitly requests a historical item.

## Authority

- Do not inspect, copy, port, or resurrect implementation, schema, workflow, or styling ideas from repository history.
- Uploaded or runtime operational reports may be used only as source contracts and read-only inputs.
- `docs/DATA_SOURCES.md` is authoritative for source structure and weighted calculations.
- `docs/IDLE_WORKFLOW.md` is authoritative for report-cycle idle accountability and queue ordering.
- `docs/WORK_LOG_HANDOFF.md` is authoritative for driver work history and deterministic handoff behavior.

## Driver and history invariants

- Driver Code is the durable driver key; Driver Name is its display identity.
- Unit Code is an assignment/object observation, never driver identity.
- Driver Leader is organizational context, never driver identity.
- Truck or leader changes never create another driver.
- Work history belongs to Driver Code and remains available when unit or leader context changes.
- Preserve the Unit Code, Driver Leader, report cycle, metrics, source, and timestamps that applied when an event occurred.
- Never silently guess, fuzzy-merge, or resolve conflicting identity/source rows.

## Work-log and handoff invariants

- Manual work statuses are `Done`, `Waiting`, and `FollowUp`.
- `Waiting` and `FollowUp` remain unresolved across launches and report cycles until explicitly resolved.
- Resolution is represented by `resolved_utc`; resolving never erases the original status, text, creation time, or context snapshots.
- Every new idle contact creates exactly one linked work entry in the same SQLite transaction.
- Existing idle events without linked work are backfilled idempotently during migration.
- A linked idle event may have at most one work entry.
- Handoff is generated only from saved work entries using the PC's local calendar-day boundary.
- Editing or copying a handoff draft never mutates work history, idle events, reports, threshold settings, or driver identity.
- Regenerate intentionally replaces the editable draft from current saved records.
- Migrations never wipe, replace, or silently recreate an existing database after failure.
- Repository fixtures and logs committed to source must contain synthetic identities only; never commit real employee data or production databases.

## Report-update invariants

- Scan/import reports automatically once during launch.
- After launch, import only through the explicit `Update Reports` action.
- Do not add `FileSystemWatcher`, periodic polling, recurring directory scans, or automatic mid-session imports.
- Preserve the last known-good roster when an import fails.
- Imported files are read-only inputs and are never modified, renamed, moved, or deleted.
- Hash accepted source content and make imports idempotent and atomic.

## Weighted-idle invariants

- Weighted 7-day idle = raw 7-day idle hours / raw 7-day engine hours.
- Weighted 28-day idle = summed idle hours / summed engine hours across the current period and three expected prior weekly periods.
- Never calculate 28-day idle by averaging weekly percentages.
- Require all four expected observations for a complete driver 28-day value; expose incomplete coverage.
- Fleet weighted values use numerator/denominator calculations and expose coverage.
- Default threshold is 50%, locally configurable, with a strict greater-than comparison.
- Either valid 7-day or complete 28-day idle above threshold puts a driver in the high-idle population.

## Idle-conversation invariants

- Conversation state is keyed by Driver Code + Report Cycle Date.
- Outcomes distinguish `Attempted`, `Spoke`, and `Spoke — Follow-up`; no event means `Not Contacted`.
- Same-cycle corrected reports preserve conversation state.
- A newer cycle derives fresh pending state without deleting prior history.
- Idle actions snapshot metrics, threshold, Unit Code, Driver Leader, source import, and timestamp.
- Queue ordering follows the four bands in `docs/IDLE_WORKFLOW.md`; unfinished high-idle work always precedes ordinary unresolved work.

## Product and performance discipline

- Keep the primary workflow on one restrained, professional WPF window; Handoff is the only secondary top-level view.
- Target low-spec Windows hardware using native WPF controls, virtualized rows, indexed queries, and short transactions.
- No browser shell, WebView, local HTTP server, Node runtime, cloud service, helper process, continuous animation, blur, glow, decorative charts, gamification, or oversized dashboard tiles.
- No one-query-per-row history or work-count loading.
- Load selected-driver work only when selection changes or saved state changes.
- Generate handoff only when entering Handoff or pressing Regenerate.
- Add a feature only when it reduces work, prevents missed follow-up, improves idle accountability, or improves handoff accuracy.

## Current product sequence

Implemented through **WAA Work Log + Handoff v0.2**:

1. Rolling 7 Day ingestion, durable roster identity, weighted driver/fleet metrics, threshold, and prioritized virtualized fleet list.
2. Per-cycle idle conversation tracking, same-cycle preservation, rollover, and ordering.
3. General driver work card with Done / Waiting / Follow-up, resolution, reopening, and carry-forward.
4. Editable deterministic Handoff with Copy to Clipboard.

Future work remains separate and must not be pulled into a maintenance change without an explicit bounded milestone:

5. Missing BOL integration.
6. Evaluate maintenance workflow separately.
7. Evaluate DOT workflow separately.
