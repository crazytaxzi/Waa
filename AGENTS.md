# WAA repository rules

WAA is a clean, driver-centric Windows work application. Current `main`, this file, README, and current `docs/` are the only implementation authority unless the user explicitly requests history.

## Authority

- Do not inspect, copy, port, or resurrect implementation/schema/UI ideas from repository history.
- Runtime reports are read-only source inputs, not product architecture.
- `docs/DATA_SOURCES.md` owns source contracts and weighted calculations.
- `docs/IDLE_WORKFLOW.md` owns idle accountability and fleet queue ordering.
- `docs/WORK_LOG_HANDOFF.md` owns work history and Handoff behavior.
- `docs/MISSING_BOL_WORKFLOW.md` owns Missing BOL import/matching/tasks/actions/source lifecycle.
- `docs/CENTRAL_WORKSPACE.md` owns one-window routing/state/keyboard responsibilities.
- `docs/THEMING.md` owns theme resources, Auto text, stream palette, semantic colors, contrast, and source auditing.

## Driver/history invariants

- Driver Code is durable identity; Driver Name is display identity.
- Unit Code is assignment context, never identity.
- Driver Leader is organizational context, never identity.
- Truck/leader changes never create another driver or move history.
- Work history belongs to Driver Code.
- Historical events preserve the Unit, Leader, report cycle, metrics, source, and timestamps that applied when saved.
- Never silently guess, fuzzy-merge, or resolve conflicting identity/source rows.

## Work-log invariants

- Manual statuses are `Done`, `Waiting`, and `FollowUp`.
- Waiting/FollowUp remain unresolved across launches/report cycles until explicitly resolved.
- `resolved_utc` is authoritative; Resolve never erases original status/text/time/context.
- Every idle contact creates exactly one linked work entry in the same SQLite transaction.
- Legacy idle events without linked work are backfilled idempotently.
- A linked idle event may have at most one work entry.
- Every matched unresolved Missing BOL item creates at most one linked `MissingBolTask`.
- Missing BOL actions create completed `MissingBolAction` activity and save atomically with item/task state.
- Generic work controls must not let MissingBolTask drift from its item; BOL Resolve/Reopen stays synchronized through Missing BOL actions.
- Migrations never wipe, replace, or silently recreate an existing database after failure.
- Committed fixtures/logs must contain synthetic identities only; never commit production employee/company data.

## Handoff invariants

- Handoff is generated only from saved work using the PC local-calendar-day boundary.
- Editing/copying the draft never mutates work, BOL, idle, reports, settings, or identity.
- An edited draft survives in-session navigation; `Regenerate` intentionally replaces it from current saved records.
- v0.4.2+ runtime Handoff is compact and driver-grouped, not the old three visible database-state sections.
- The generated opening `No open ACE/ACI's` is an editable user-requested convention only. WAA does **not** model or validate ACE/ACI state; never claim otherwise.
- Runtime narrative emits at most one line per driver, ordered by Driver Name then Driver Code.
- Prefer current fleet Unit Code and Driver Name in Handoff identity when available. Useful historical Unit snapshot is only fallback; never print blank or `*` as a unit.
- Idle narrative may omit generated 28D/7D metric boilerplate but must preserve underlying saved metrics/events and retain the human note.
- MissingBolAction narrative prefers the human-entered note when present; do not invent coaching state or other facts not stored.
- Runtime Handoff contains a dedicated `Missing BOLs:` section. Each driver appears once there with all unresolved matched BOL Order # values grouped on one line.
- The compact Missing BOL Handoff intentionally omits Empty Call Date, route, and local status; those details stay in the focused BOL workspace.
- Do not reintroduce visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, or `COMPLETED TODAY` sections into the runtime draft unless explicitly requested. Underlying deterministic classification may remain for tests/compatibility.

## Central workspace invariants

- One `MainWindow` is the operational shell.
- Driver/task/Handoff/unmatched views render inside one central content host, never separate top-level Windows.
- Fresh launch starts at Fleet Queue.
- Fleet row opens Driver Workspace by durable Driver Code; Unit Code never owns route identity.
- Actionable work opens one focused task workspace.
- The old always-visible selected-driver split pane must not return or remain as a hidden parallel path.
- Driver Workspace is a work index, not a pile of every editor.
- Back preserves actual session context; task Back returns to prior Driver Workspace.
- Queue search/selection survive Fleet → Driver → Fleet round trips.
- New Work/BOL unsaved text must not be silently discarded by navigation/report refresh.
- Handoff draft survives navigation until explicit Regenerate.
- Report refresh restores routes only through stable Driver Code/item/work IDs; stale entities fail gracefully with a return path.
- `Next Work Item` uses current-driver deterministic ordering, then reuses search-respecting `Next Needing Attention`; no competing fleet priority engine.
- Activity Detail and Unmatched Missing BOL are read-only.
- `Alt+Left` may perform Back only when it does not hijack text editing.
- Clickable rows must be keyboard accessible and status must never rely on color alone.
- Fleet Queue Up/Down remains native DataGrid row navigation; Enter opens the focused row.
- Fleet Queue keeps row/column virtualization, recycling, and content scrolling enabled.

## Theme invariants

- Ordinary text inherits active theme through `TextBrush` and implicit/base WPF styles.
- No fixed ordinary text colors in MainWindow/UserControls/view-models/converters/code-behind/templates.
- Literal theme colors belong only in dedicated Light/Dark palettes or an explicitly equivalent centralized implementation.
- Light/Dark palettes must contain matching required keys.
- Theme styles use `DynamicResource` so live switching does not require restart/navigation reset.
- Subtle/Disabled/Primary/Selected/Link/semantic/focus/editor colors are theme-aware.
- v0.4.3 purple is the primary selection/focus/breadcrumb/Handoff highlight role; green is the positive/completed/Next Needing Attention role; ordinary text remains neutral.
- No glow, blur, decorative animation, or one-off view color is part of the stream palette.
- DataGrid generated text explicitly follows active cell/theme foreground and must not fall back to system black.
- Semantic state belongs in flags/enums; view-models do not expose WPF Brush/Color objects.
- Theme preference remains explicit Light/Dark. “Auto text” means inheritance, not a third System/Auto mode.
- Preference persistence must not block UI; failed save restores prior visible theme.
- Normal text contrast >= 4.5:1; relevant boundaries/focus >= 3:1.
- Keep the repository-wide hard-coded-theme source audit and contrast tests; fix causes rather than weakening tests.
- Any data-bound WPF `Run.Text` must explicitly use a safe one-way display binding; repository regression coverage protects this startup/runtime rule.

## Report-update invariants

- Scan/import reports automatically once at launch.
- After launch import only through explicit `Update Reports`.
- No `FileSystemWatcher`, periodic polling, recurring scan, or automatic mid-session import.
- Rolling 7 Day and Missing BOL update independently; one source failure never erases/rolls back the other accepted state.
- Preserve last-known-good state after source failure.
- Imported files are read-only and never modified/moved/renamed/deleted.
- Accepted source content is hashed; imports are idempotent and atomic.

## Missing BOL invariants

- `Order #` is durable Missing BOL item identity.
- Match only normalized exact `Last Dispatch Driver cd` to normalized exact Driver Code.
- Exact normalization = trim + uppercase invariant text; preserve leading zeros; never convert codes to integers.
- Never match by name, Unit, Leader, truck, substring, prefix, similarity, probability, or fuzzy logic.
- Source driver name is evidence only and never overwrites durable Driver Name.
- Unknown/blank source codes stay visible unmatched and create no driver-owned task.
- Later exact roster match attaches item and creates task exactly once.
- Source disappearance never resolves item/task.
- Resolved item reappearing remains resolved until explicit Reopen.
- Existing Order # changing to another source Driver Code is a conflict; reject snapshot rather than moving history.
- One item creates at most one linked task. Reimport/Reopen never create another.
- Requested/Attempted/Follow-up/Resolved/Reopen append history; never overwrite prior events.
- Unmatched BOL is read-only; no manual/fuzzy assignment.

## Weighted-idle and queue invariants

- 7-day idle = raw idle hours / raw engine hours.
- 28-day idle = summed idle hours / summed engine hours across current and exact -7/-14/-21 day periods.
- Never average weekly percentages.
- Complete driver 28-day requires all four expected observations; expose incomplete coverage.
- Fleet weighted values also use numerator/denominator calculations and expose coverage.
- Default threshold 50%, strict greater-than comparison.
- Either valid 7-day or complete 28-day above threshold puts driver in high-idle population.
- Idle state is keyed by Driver Code + Report Cycle Date.
- Outcomes distinguish Attempted, Spoke, Spoke — Follow-up; no event means Not Contacted.
- Same-cycle correction preserves conversation state; new cycle derives fresh pending state without deleting history.
- Idle actions snapshot metrics/threshold/Unit/Leader/source/time.
- Queue ordering follows `docs/IDLE_WORKFLOW.md`; unfinished high-idle work always precedes ordinary unresolved work including BOL.

## Permanent exclusions

Unless explicitly reversed by the user, do not add or create placeholders for:

- emailing/transmitting documents
- automatic calls/messages/driver contact
- OCR/image recognition
- document upload/storage/attachment/document-management workflows
- giant BOL dashboards or separate analytics portals
- BOL revenue/financial analytics
- fuzzy/name/unit/truck/leader/probabilistic identity matching
- complex escalation trees, routing engines, approval workflows
- browser/WebView/local server/Node/cloud/helper processes
- background report polling/watchers
- decorative animation, blur, glow, or gamification

## Product/performance discipline

- Keep operational work inside one restrained native WPF MainWindow.
- Target low-spec Windows hardware with virtualized rows, indexed aggregate queries, bounded selected-entity reads, and short transactions.
- No one-query-per-row work/history/BOL count paths.
- Selected-driver work/BOL state loads only on selection/saved-state/route refresh, not each keystroke.
- Load focused task detail only when task opens.
- Report/database work that can block runs off the UI thread.
- Generate Handoff only on first session entry or explicit Regenerate.
- Keep queue virtualization/recycling enabled.
- Do not retain hidden legacy split-pane controls or duplicate query paths.

## Current product sequence

Implemented through **WAA v0.4.3**:

1. Rolling 7 Day ingestion, durable roster identity, weighted metrics, threshold, prioritized virtualized fleet.
2. Current-cycle idle accountability and deterministic queue ordering.
3. Driver work log with Done/Waiting/Follow-up, Resolve/Reopen, carry-forward, linked idle work.
4. Editable deterministic Handoff with Copy to Clipboard.
5. Missing BOL managed XLSX ingestion, exact-code matching, unmatched visibility, one linked task/item, atomic actions, queue/search/Handoff integration.
6. One-window Fleet → Driver → Task workspace with Back/breadcrumb state preservation, focused task routes, Next Work Item, centralized Handoff/Unmatched routes, stale-route handling.
7. Central Light/Dark palettes, automatic theme-safe text, semantic colors, DataGrid generated-text handling, source audit, deterministic contrast validation.
8. v0.4.1 startup binding hotfix: all data-bound inline WPF Run text is explicit one-way display binding with regression protection.
9. v0.4.2 compact Handoff: alphabetical one-line driver narratives plus grouped `Missing BOLs:` order lists.
10. v0.4.3 presentation refinement: denser virtualized Fleet Queue, compact Driver Code/Unit identity without duplicate Leader text, full-row click/Enter preserved, and centralized gunmetal/neon-purple/neon-green contrast-safe stream palette.

Future work remains separate unless explicitly requested:

11. Maintenance evaluation.
12. DOT evaluation.
