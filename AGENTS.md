# WAA repository rules

WAA is a clean, driver-centric Windows work application. Current `main`, this file, README, and current `docs/` are the only implementation authority unless the user explicitly requests history.

## Authority

- Do not inspect, copy, port, or resurrect implementation/schema/UI ideas from repository history.
- Runtime reports are read-only source inputs, not product architecture.
- `docs/DATA_SOURCES.md` owns source contracts and weighted calculations.
- `docs/IDLE_WORKFLOW.md` owns idle accountability and fleet queue ordering.
- `docs/WORK_LOG_HANDOFF.md` owns saved work history and Handoff behavior.
- `docs/MISSING_BOL_WORKFLOW.md` owns the source-only Missing BOL workbook view, exact matching, unmatched handling, and Handoff projection.
- `docs/CENTRAL_WORKSPACE.md` owns one-window routing/state/keyboard responsibilities.
- `docs/THEMING.md` owns theme resources, Auto text, stream palette, ambient motion, semantic colors, contrast, and source auditing.

## Driver/history invariants

- Driver Code is durable identity; Driver Name is display identity.
- Unit Code is assignment context, never identity.
- Driver Leader is organizational context, never identity.
- Truck/leader changes never create another driver or move history.
- Saved work history belongs to Driver Code.
- Historical saved events preserve the Unit, Leader, report cycle, metrics, source, and timestamps that applied when saved.
- Never silently guess, fuzzy-merge, or resolve conflicting identity/source rows.

## Work-log invariants

- Manual statuses are `Done`, `Waiting`, and `FollowUp`.
- Waiting/FollowUp remain unresolved across launches/report cycles until explicitly resolved.
- `resolved_utc` is authoritative; Resolve never erases original status/text/time/context.
- Every idle contact creates exactly one linked work entry in the same SQLite transaction.
- Legacy idle events without linked work are backfilled idempotently.
- A linked idle event may have at most one work entry.
- v0.4.6+ Missing BOL rows do not create work entries, Today’s Activity, local status, action events, notes, or resolution history.
- Legacy v0.3-v0.4.5 `MissingBolTask` / `MissingBolAction` work may remain physically in an upgraded database but must be classified and excluded from current Open Work, Today’s Activity, queue priority, and ordinary Handoff narrative.
- Migrations never wipe, replace, or silently recreate an existing database after failure.
- Committed fixtures/logs must contain synthetic identities only; never commit production employee/company data.

## Handoff invariants

- Handoff is generated from saved non-BOL work using the PC local-calendar-day boundary plus a transient projection of the **current Missing BOL workbook**.
- Editing/copying the draft never mutates work, BOL, idle, reports, settings, or identity.
- An edited draft survives in-session navigation; `Regenerate` intentionally replaces it from current saved work/current BOL rows.
- v0.4.2+ runtime Handoff is compact, not the old three visible database-state sections.
- The generated opening `No open ACE/ACI's` is an editable user-requested convention only. WAA does **not** model or validate ACE/ACI state; never claim otherwise.
- v0.4.4+ runtime Handoff separates represented drivers under `Driver Leader: ...` headings. Leader headings sort alphabetically; drivers within a leader sort by Driver Name then Driver Code.
- Runtime ordinary work narrative emits at most one line per driver.
- Prefer current fleet Driver Leader for Handoff grouping when available; useful saved `driver_leader_snapshot` is fallback for saved non-BOL work; blank/`*` leader is not a real group and falls back to `Unassigned` only when no meaningful leader exists.
- Prefer current fleet Unit Code and Driver Name in Handoff identity when available. Useful historical Unit snapshot is only fallback for saved work; never print blank or `*` as a unit.
- Driver Leader grouping is presentation only and never changes durable Driver Code ownership or historical snapshots.
- Idle narrative may omit generated 28D/7D metric boilerplate but must preserve underlying saved metrics/events and retain the human note.
- Runtime Handoff contains a dedicated `Missing BOLs:` section generated from current matched workbook rows. Each represented driver uses current Driver Code ownership and the same Driver Leader grouping; each driver appears once there with current-file Order # values grouped on one line.
- Current BOL rows do not contribute ordinary saved-work narrative and do not create Handoff history in SQLite.
- The compact Missing BOL Handoff intentionally omits Empty Call Date, route, and local status; local BOL status no longer exists in v0.4.6.
- Do not reintroduce visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, or `COMPLETED TODAY` sections into the runtime draft unless explicitly requested.

## Central workspace invariants

- One `MainWindow` is the operational shell.
- Driver/task/Handoff/unmatched views render inside one central content host, never separate top-level Windows.
- Fresh launch starts at Fleet Queue.
- Fleet row opens Driver Workspace by durable Driver Code; Unit Code never owns route identity.
- Actionable work opens one focused task workspace.
- Current Missing BOL rows are read-only information, not actionable work; they live in a dedicated `CURRENT MISSING BOL` section and may open a read-only same-window detail.
- The old always-visible selected-driver split pane must not return or remain as a hidden parallel path.
- Driver Workspace is a work index plus bounded current-report context, not a pile of every editor.
- Back preserves actual session context; task/detail Back returns to prior Driver Workspace.
- Queue search/selection survive Fleet → Driver → Fleet round trips.
- New Work unsaved text must not be silently discarded by navigation/report refresh.
- Handoff draft survives navigation until explicit Regenerate.
- Report refresh restores routes only through stable Driver Code/item/work IDs; stale entities fail gracefully with a return path.
- `Next Work Item` walks actionable idle/manual work only, then reuses search-respecting `Next Needing Attention`; current Missing BOL rows are excluded.
- Activity Detail, Missing BOL detail, and Unmatched Missing BOL are read-only.
- `Alt+Left` may perform Back only when it does not hijack text editing.
- Clickable rows must be keyboard accessible and status must never rely on color alone.
- Fleet Queue Up/Down remains native DataGrid row navigation; Enter opens the focused row.
- Fleet Queue keeps row/column virtualization, recycling, and content scrolling enabled.

## Theme and motion invariants

- Ordinary text inherits active theme through `TextBrush` and implicit/base WPF styles.
- No fixed ordinary text colors in MainWindow/UserControls/view-models/converters/code-behind/templates.
- Literal theme colors belong only in dedicated Light/Dark palettes or an explicitly equivalent centralized implementation.
- Light/Dark palettes must contain matching required keys.
- Theme styles use `DynamicResource` so live switching does not require restart/navigation reset.
- MainWindow and its root client surface explicitly consume `WindowBackgroundBrush` through `DynamicResource`; exposed shell/margin space must switch with the active palette.
- Subtle/Disabled/Primary/Selected/Link/semantic/focus/editor colors are theme-aware.
- Purple is the primary selection/focus/breadcrumb/Handoff highlight role; green is the positive/completed/Next Needing Attention role; ordinary text remains neutral.
- v0.4.5 permits only the bounded ambient-motion layer approved by the user: one faint scanline, a small fixed set of sparse electric-blue motes, and restrained button hover/press motion.
- Ambient effects run only in Dark mode and only when the current WAA ambient-motion state is enabled.
- When no WAA ambient-motion preference has ever been saved, Windows `SystemParameters.ClientAreaAnimation` may seed the initial runtime default. It must never permanently disable or grey out the WAA motion control.
- Once the user explicitly chooses motion on/off, that persisted WAA preference is authoritative on later launches even when Windows/RDP/enterprise policy reports client-area animation disabled.
- Ambient motion must remain non-interactive, palette-driven, timer-free, dependency-free, and cheap on low-spec hardware. No per-frame particle creation, blur, glow, shader, background worker, or animation over DataGrid rows/editable text.
- The ambient-motion preference uses the existing `settings` table and requires no database schema version change.
- Button motion must remain render-only and subtle; do not alter layout, keyboard behavior, hit targets, commands, or semantic colors.
- DataGrid generated text explicitly follows active cell/theme foreground and must not fall back to system black.
- Semantic state belongs in flags/enums; view-models do not expose WPF Brush/Color objects.
- Theme preference remains explicit Light/Dark. “Auto text” means inheritance, not a third System/Auto mode.
- Preference persistence must not block UI; failed save restores prior visible state.
- Normal text contrast >= 4.5:1; relevant boundaries/focus >= 3:1.
- Keep the repository-wide hard-coded-theme source audit and contrast tests; fix causes rather than weakening tests.
- Any data-bound WPF `Run.Text` must explicitly use a safe one-way display binding; repository regression coverage protects this startup/runtime rule.

## Report-update invariants

- Scan reports automatically once at launch.
- After launch scan only through explicit `Update Reports`.
- No `FileSystemWatcher`, periodic polling, recurring scan, or automatic mid-session import.
- Rolling 7 Day and Missing BOL update independently; one source failure never rolls back the other source’s accepted state.
- Rolling 7 Day preserves last-known-good saved roster after source failure.
- Missing BOL is source-only: current rows exist only after a valid current-session workbook scan and are never restored from SQLite.
- Imported/source files are read-only and never modified/moved/renamed/deleted.
- Rolling accepted source hashes remain persisted/idempotent/atomic; Missing BOL SHA-256 is same-session change detection only and is not persisted.

## Missing BOL invariants

- Missing BOL is a read-only current-workbook view; no current BOL rows/import metadata/status/actions/notes/history/tasks are written to SQLite.
- `Order #` is the normalized current source-row identity used for duplicate validation and stable in-session item routing.
- Match only normalized exact `Last Dispatch Driver cd` to normalized exact **current** Driver Code.
- Exact normalization = trim + uppercase invariant text; preserve leading zeros; never convert codes to integers.
- Never match by name, Unit, Leader, truck, substring, prefix, similarity, probability, or fuzzy logic.
- Source driver name is evidence only and never overwrites durable Driver Name.
- Unknown/blank source codes stay visibly unmatched and create no driver-owned work.
- If a later scan/current roster contains the exact Driver Code, the current row may then appear under that driver; there is no persistent attach operation.
- Source disappearance means the row disappears from WAA after the next accepted scan; it is not locally resolved or carried forward.
- Requested/Attempted/Follow-up/Resolved/Reopen BOL state does not exist in v0.4.6+.
- Current matched BOL rows do not increase Open Work, driver attention priority, Today’s Activity, or `Next Work Item`.
- Driver Workspace presents current matched rows in a dedicated read-only `CURRENT MISSING BOL` section.
- Handoff projects current matched rows transiently at Regenerate time; no BOL Handoff state is stored.
- Unmatched BOL is read-only; no manual/fuzzy assignment.
- Fresh v0.4.6+ databases do not create `missing_bol_*` tables.
- Upgraded databases may retain old `missing_bol_*` tables/linked BOL work physically; do not destructively drop them during normal upgrade, but do not let them drive current work/report state.

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
- Queue ordering follows `docs/IDLE_WORKFLOW.md`; unfinished high-idle work always precedes ordinary unresolved saved work. Current Missing BOL rows do not participate in priority ordering.

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
- heavy decorative animation, blur, glow, shaders, particle engines, or gamification beyond the explicitly approved bounded v0.4.5 ambient layer

## Product/performance discipline

- Keep operational work inside one restrained native WPF MainWindow.
- Target low-spec Windows hardware with virtualized rows, indexed aggregate saved-work queries, bounded selected-entity reads, short transactions, and bounded ambient rendering.
- Current Missing BOL counts/search/detail derive from one bounded in-memory workbook snapshot; do not introduce one-query-per-row BOL database paths.
- Selected-driver work/BOL presentation loads only on selection/report/route refresh, not each keystroke.
- Load focused detail only when opened.
- Report/database work that can block runs off the UI thread.
- Generate Handoff only on first session entry or explicit Regenerate.
- Keep queue virtualization/recycling enabled.
- Do not retain hidden legacy split-pane controls or duplicate query paths.

## Current product sequence

Implemented through **WAA v0.4.6**:

1. Rolling 7 Day ingestion, durable roster identity, weighted metrics, threshold, prioritized virtualized fleet.
2. Current-cycle idle accountability and deterministic queue ordering.
3. Driver work log with Done/Waiting/Follow-up, Resolve/Reopen, carry-forward, linked idle work.
4. Editable deterministic Handoff with Copy to Clipboard.
5. Missing BOL managed XLSX ingestion and exact Driver Code source matching.
6. One-window Fleet → Driver → Task workspace with Back/breadcrumb state preservation, focused task routes, Next Work Item, centralized Handoff/Unmatched routes, stale-route handling.
7. Central Light/Dark palettes, automatic theme-safe text, semantic colors, DataGrid generated-text handling, source audit, deterministic contrast validation.
8. v0.4.1 startup binding hotfix: all data-bound inline WPF Run text is explicit one-way display binding with regression protection.
9. v0.4.2 compact Handoff: alphabetical one-line driver narratives plus grouped `Missing BOLs:` order lists.
10. v0.4.3 presentation refinement: denser virtualized Fleet Queue, compact Driver Code/Unit identity without duplicate Leader text, full-row click/Enter preserved, and centralized gunmetal/neon-purple/neon-green contrast-safe stream palette.
11. v0.4.4 presentation refinement: Driver Leader-separated Handoff narrative/Missing BOL groups with current-leader preference and historical fallback, plus explicit MainWindow/root-shell dynamic background binding for complete dark-mode coverage.
12. v0.4.5 ambient motion: dark-only faint scanline, eight sparse electric-blue motes, persisted Motion control, and restrained template-local button hover/press feedback.
13. v0.4.5.1 motion-control hotfix: Windows reduced-animation state seeds only the unsaved first-run default; the WAA Motion button always remains usable and an explicit WAA on/off choice overrides later Windows/RDP animation flags.
14. v0.4.6 source-only Missing BOL: current workbook rows are held only in memory, displayed read-only, excluded from Open Work/priority/Next Work, and projected transiently into Handoff; legacy BOL DB data remains dormant/non-destructive.

Future work remains separate unless explicitly requested:

15. Maintenance evaluation.
16. DOT evaluation.