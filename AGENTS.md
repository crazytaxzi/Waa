# WAA repository rules

WAA is a clean, driver-centric Windows work application. The current `main` tree, this file, the README, and the current documents under `docs/` are the only implementation authority unless the user explicitly requests a historical item.

## Authority

- Do not inspect, copy, port, or resurrect implementation, schema, workflow, or styling ideas from repository history.
- Uploaded or runtime operational reports may be used only as source contracts and read-only inputs.
- `docs/DATA_SOURCES.md` is authoritative for report structures, field precedence, and weighted calculations.
- `docs/IDLE_WORKFLOW.md` is authoritative for report-cycle idle accountability and queue ordering.
- `docs/WORK_LOG_HANDOFF.md` is authoritative for driver work history and deterministic handoff behavior.
- `docs/MISSING_BOL_WORKFLOW.md` is authoritative for Missing BOL ingestion, matching, tasks, actions, unmatched items, and source lifecycle.
- `docs/CENTRAL_WORKSPACE.md` is authoritative for v0.4 one-window navigation, route/state preservation, keyboard behavior, and workspace responsibilities.
- `docs/THEMING.md` is authoritative for v0.4 theme resource ownership, automatic text, semantic colors, contrast, and theme-source auditing.

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
- Every matched unresolved Missing BOL item creates at most one linked `MissingBolTask` work entry.
- Missing BOL actions create completed `MissingBolAction` activity entries and save atomically with item/task state.
- Generic Open Work controls must not let a linked Missing BOL task drift from its item; resolve/reopen remains synchronized through Missing BOL actions.
- Handoff is generated only from saved work entries using the PC's local calendar-day boundary.
- Editing or copying a Handoff draft never mutates work history, BOL state, idle events, reports, threshold settings, or driver identity.
- Navigating away from an edited Handoff draft and back in the same session must preserve the draft; `Regenerate` intentionally replaces it from current saved records.
- Migrations never wipe, replace, or silently recreate an existing database after failure.
- Repository fixtures and logs committed to source must contain synthetic identities only; never commit real employee data, production reports, or production databases.

## Central workspace invariants

- One `MainWindow` is the operational shell.
- Driver/task/Handoff/unmatched views render inside the shell's central content host; they do not create additional top-level WPF Windows.
- The default/fresh-launch route is Fleet Queue.
- A fleet row opens Driver Workspace for durable Driver Code; Unit Code never owns route identity.
- An actionable driver-work row opens one focused task workspace.
- The old always-visible selected-driver split pane must not be reintroduced or kept as a hidden parallel workflow.
- Driver Workspace is primarily a work index. Do not put every task editor directly on it.
- Back navigation must preserve actual session context. Task Back returns to its prior Driver Workspace rather than restarting the application workflow.
- Queue search and selection survive Fleet → Driver → Fleet round trips.
- New Work and Missing BOL unsaved text must not be silently discarded by in-session navigation or report refresh.
- Handoff draft survives navigation away/back until `Regenerate` is intentionally pressed.
- Report refresh may rebuild a current route only through stable Driver Code/item/work-entry IDs. Stale entities fail gracefully with an explicit return path.
- `Next Work Item` uses the current driver’s deterministic work list and then reuses existing search-respecting `Next Needing Attention`; do not create a competing fleet priority engine.
- Activity Detail and Unmatched Missing BOL are read-only.
- `Alt+Left` may perform Back only when it does not hijack text editing.
- Clickable rows must also be keyboard-accessible and must expose status in words, not color alone.

## Theme invariants

- Ordinary application text inherits the active theme through `TextBrush` and implicit/base WPF styles.
- Do not set fixed ordinary text colors in MainWindow, UserControls, view-models, converters, code-behind, or data templates.
- Literal theme colors belong only in the dedicated Light/Dark palette dictionaries or an explicitly equivalent centralized palette implementation.
- Light and Dark palette dictionaries must have matching required key sets.
- Theme-related style values use `DynamicResource` so live Light/Dark switching updates the current visible route without restart or navigation reset.
- `SubtleTextBrush`, `DisabledTextBrush`, `PrimaryButtonTextBrush`, `SelectedRowTextBrush`, `LinkTextBrush`, semantic brushes, focus/border brushes, and editor colors are theme-aware.
- DataGrid-generated `DataGridTextColumn` display/edit elements must explicitly follow the active cell/theme foreground and may not fall back to system black text.
- Semantic state belongs in view-model flags/enums; view-models must not expose WPF Brush/Color objects.
- Warning, Follow-up, Completed, Quiet, Error, and Information colors must be independently readable in both palettes.
- Theme preference remains explicit Light/Dark, persisted locally. “Auto text” means resource inheritance, not a third System/Auto appearance mode.
- Theme persistence must not block the UI thread. If saving the preference fails after a live switch, restore the prior visible theme.
- Normal text combinations must meet at least 4.5:1 contrast; relevant UI boundaries/focus indicators must meet at least 3:1.
- Keep the repository-level source audit that rejects hard-coded UI theme colors outside palette files. Fix palette/style failures; do not weaken/delete the audit or contrast tests.

## Report-update invariants

- Scan/import reports automatically once during launch.
- After launch, import only through the explicit `Update Reports` action.
- Do not add `FileSystemWatcher`, periodic polling, recurring directory scans, or automatic mid-session imports.
- Rolling 7 Day and Missing BOL update independently; failure in one source never erases or rolls back the other source's accepted state.
- Preserve the last known-good roster and last known-good Missing BOL snapshot when an import fails.
- Imported files are read-only inputs and are never modified, renamed, moved, or deleted.
- Hash accepted source content and make imports idempotent and atomic.

## Missing BOL invariants

- `Order #` is the durable Missing BOL source-item key.
- Match only normalized exact `Last Dispatch Driver cd` to normalized exact Driver Code.
- Exact normalization is trim + uppercase invariant text; preserve leading zeros and never convert driver codes to integers.
- Never match by name, Unit Code, Driver Leader, truck, substring, prefix, similarity, probability, or any fuzzy method.
- `Last Dispatch Driver nm` is supporting evidence only and never overwrites durable Driver Name.
- Unknown or blank source Driver Codes remain visible as unmatched and create no driver-owned task.
- If a later roster introduces the exact Driver Code, attach the item and create its task exactly once.
- Disappearance from a later workbook never resolves an item or its task.
- A resolved item that reappears remains resolved, is marked present again, and may be explicitly reopened.
- A later source row that moves an existing Order # to a different normalized Driver Code is a conflict; reject the snapshot rather than moving history.
- One item creates at most one linked task. Reimport and Reopen never create a second task.
- Requested, Attempted, Follow-up, Resolved, and Reopen append action history; they never overwrite prior events.
- Missing BOL remains a compact workflow inside the central driver/task workspaces, unified work log, and Handoff.
- Unmatched BOL remains read-only; do not add manual/fuzzy assignment.

## Weighted-idle invariants

- Weighted 7-day idle = raw 7-day idle hours / raw 7-day engine hours.
- Weighted 28-day idle = summed idle hours / summed engine hours across the current period and three expected prior weekly periods.
- Never calculate 28-day idle by averaging weekly percentages.
- Require all four expected observations for a complete driver 28-day value; expose incomplete coverage.
- Fleet weighted values use numerator/denominator calculations and expose coverage.
- Default threshold is 50%, locally configurable, with a strict greater-than comparison.
- Either valid 7-day or complete 28-day idle above threshold puts a driver in the high-idle population.

## Idle-conversation and queue invariants

- Conversation state is keyed by Driver Code + Report Cycle Date.
- Outcomes distinguish `Attempted`, `Spoke`, and `Spoke — Follow-up`; no event means `Not Contacted`.
- Same-cycle corrected reports preserve conversation state.
- A newer cycle derives fresh pending state without deleting prior history.
- Idle actions snapshot metrics, threshold, Unit Code, Driver Leader, source import, and timestamp.
- Queue ordering follows the four bands in `docs/IDLE_WORKFLOW.md`; unfinished high-idle work always precedes ordinary unresolved work, including Missing BOL tasks.
- Within otherwise equal ordinary unresolved work, the oldest open Missing BOL Empty Call Date may break ties before stable Driver Name and Driver Code ordering.

## Permanent product exclusions

The following capabilities are permanently outside WAA's intended scope unless the user explicitly reverses this decision:

- emailing or transmitting documents
- automatic calls, messages, or driver contact
- OCR or document-image recognition
- document upload, storage, attachment, or document-management workflows
- giant Missing BOL dashboards or separate analytics portals
- financial/BOL revenue analytics
- fuzzy identity matching, fuzzy record linking, name similarity, or probabilistic merges
- truck-, Unit Code-, or Driver-Leader-based identity
- complex escalation trees, routing engines, approval workflows, or multi-level escalation logic
- browser dashboards, WebView, local web servers, Node, cloud services, or helper processes
- background report polling/watchers
- decorative animation, blur, glow, or gamification

Do not add placeholders, abstractions, schema, services, buttons, or documentation promises for excluded capabilities. Missing BOL may import source evidence, attach through exact Driver Code, create/update local work status, affect ordinary-work priority, and feed the existing work log and Handoff. Nothing more.

## Product and performance discipline

- Keep all operational work inside one restrained, professional native WPF `MainWindow`.
- Target low-spec Windows hardware using virtualized rows, indexed aggregate queries, bounded selected-entity reads, and short transactions.
- No one-query-per-row history, work-count, or BOL-count loading.
- Load selected-driver work/BOL state only for selection/saved-state/route refresh, not every keystroke.
- Load a focused task’s additional detail (for example BOL action history) only when that task opens.
- Parse reports and perform database/report operations off the UI thread where the operation can block.
- Generate Handoff only on first session entry or explicit Regenerate; do not regenerate on every navigation return or edit.
- Keep queue row virtualization/recycling enabled.
- Do not keep hidden legacy split-pane controls or duplicate query paths.
- Add a feature only when it reduces work, prevents missed follow-up, improves idle accountability, improves BOL accountability, or improves Handoff accuracy.

## Current product sequence

Implemented through **WAA Central Workspace + Theme-Safe Text v0.4**:

1. Rolling 7 Day ingestion, durable roster identity, weighted driver/fleet metrics, threshold, and prioritized virtualized fleet list.
2. Per-cycle idle conversation tracking, same-cycle preservation, rollover, and ordering.
3. Driver work log with Done / Waiting / Follow-up, resolution, reopening, carry-forward, and linked idle work.
4. Editable deterministic Handoff with Copy to Clipboard.
5. Missing BOL managed XLSX ingestion, exact-code matching, unmatched visibility, one linked task per item, atomic local actions, queue/search integration, and deterministic Handoff integration.
6. One-window central Fleet → Driver → Task workspace with session-safe Back/breadcrumb navigation, focused task views, Next Work Item, centralized Handoff/Unmatched routes, and stale-route handling.
7. Centralized Light/Dark palettes, automatic theme-safe text inheritance, semantic theme colors, DataGrid generated-text handling, source audit, and deterministic contrast validation.

Future work remains separate and must not be pulled into a maintenance change without an explicit bounded milestone:

8. Evaluate maintenance workflow separately.
9. Evaluate DOT workflow separately.
