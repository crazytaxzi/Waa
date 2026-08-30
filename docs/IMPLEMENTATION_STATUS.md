# WAA Implementation Status

## Current bounded release

**WAA v0.4.2 — compact driver-grouped Handoff release candidate, branch Windows-validated.**

Current validated PR-tree workflow: **Windows build, test, and portable package #60**, run ID `33342823508`, August 30, 2026.

Final release still requires this validation-record commit to pass on PR #4, then merged `main` to pass before the portable artifact is delivered.

## Runtime and deployment

- .NET 8 native WPF
- one top-level `MainWindow`
- warnings treated as errors
- Windows x64 self-contained portable publish
- no installer/admin/SDK/separate .NET/Excel/Office requirement in published build
- one local desktop process
- SQLite/preferences under `%LOCALAPPDATA%\WAA`
- GitHub Actions restores, builds/WPF-compiles, tests, publishes, and uploads artifact

## Central workspace

Current same-window routes:

- FleetQueue
- DriverWorkspace
- IdleTask
- MissingBolTask
- WorkItemTask
- NewWork
- ActivityDetail
- Handoff
- UnmatchedBol
- Unavailable

The old fleet + selected-driver split pane is removed. Driver route identity is durable Driver Code; task routes use persisted item/work IDs. Fleet row click/Enter opens Driver Workspace. Back/breadcrumbs preserve real session context. Alt+Left is safe outside text editors. Search, selected driver, New Work drafts, BOL notes, and edited Handoff survive in-session navigation. Report refresh restores valid routes by stable IDs and stale entities fail to explicit Unavailable state.

Driver Workspace is a compact work index. Idle and linked idle work appear once. Each unresolved BOL appears once rather than duplicating MissingBolTask as manual work. Manual Waiting/Follow-up appears once. Focused task pages preserve existing business/transaction rules.

`Next Work Item` ordering remains:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual Follow-up
4. manual Waiting
5. other supported unresolved manual work

Then existing visible/search-respecting `Next Needing Attention` is reused.

## Theme-safe text and startup binding safety

- Light/Dark literal colors are centralized in palette dictionaries
- BaseStyles consumes theme roles dynamically
- ordinary/generated DataGrid/editor/button/selected/disabled/semantic text is theme-aware
- live switching does not reset route/session state
- appearance preference persists locally off UI thread with visible rollback on failure
- repository source audit blocks inappropriate fixed theme colors
- deterministic contrast tests cover normal/semantic/selected/editor/hover/focus cases
- all data-bound inline WPF `Run.Text` uses explicit one-way display binding
- repository regression test prevents the v0.4 startup binding failure from returning

## v0.4.2 compact Handoff

Runtime generated Handoff now matches the requested operational shape rather than a verbose database report.

Opening convention:

`No open ACE/ACI's`

Important limitation: WAA does **not** model or validate ACE/ACI state. The line is intentionally editable and must be changed by the user when untrue.

Driver narrative:

- at most one narrative line per driver
- alphabetical Driver Name / Driver Code ordering
- current fleet Unit Code and Driver Name preferred when available
- unresolved non-BOL work + current-day activity can combine on one driver line
- idle prose retains concise action + human note but omits generated 28D/7D metric boilerplate from copied Handoff
- underlying idle metric snapshots remain saved/intact
- MissingBolAction narrative prefers human note when present
- WAA does not invent coached/not-coached state or any unstored fact

Missing BOL section:

- dedicated `Missing BOLs:` heading
- each driver appears once
- all unresolved matched Order # values are grouped on that line
- singular/plural wording handled automatically
- deterministic oldest Empty Call Date then Order # ordering
- copied section omits Empty Call Date, route, and local status; full details remain in Missing BOL Task
- no fuzzy/manual assignment or BOL state logic changed

The runtime draft no longer shows visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, or `COMPLETED TODAY` headings. Underlying deterministic open/completed/local-day classification remains regression-tested.

Edited draft isolation remains unchanged: navigating away/back preserves edit, Regenerate intentionally replaces it, Copy copies current edit, and no Handoff edit mutates saved work/BOL/idle/report/settings state.

## Database compatibility

v0.4.2 requires **no database schema change** and does not increment schema version.

Existing `%LOCALAPPDATA%\WAA` remains compatible and preserves:

- roster/import metadata/observations
- idle contacts
- work entries and linked idle work
- Missing BOL imports/items/actions/work links
- threshold
- Light/Dark preference
- Handoff source history

Replacing the portable application folder leaves the data folder intact.

## Preserved business/data regression result

Still passing:

- Rolling 7 Day import/normalization
- weighted 7-day and complete-coverage 28-day calculations
- threshold persistence/reranking
- idle actions + linked work
- unresolved carry-forward
- Resolve/Reopen
- Missing BOL parsing/import
- exact-code BOL matching and unmatched preservation
- BOL actions/task synchronization/action history/source lifecycle
- report launch/manual update restrictions
- ten-character Driver Leader support
- no watcher/polling path
- portable Windows publish
- theme/source audit/contrast
- central navigation/state/keyboard/stale-route behavior
- v0.4.1 Run.Text startup binding safety

No permanent exclusion was introduced.

## Current validation

PR #4 documented-tree precursor, workflow **#60**, run ID `33342823508`:

- restore: passed
- warnings-as-errors Release build: passed
- WPF/XAML compilation: passed
- core tests: **24 passed**
- app/SQLite/navigation/theme/source-audit/Handoff/integration tests: **167 passed**
- total: **191 passed, 0 failed, 0 skipped**
- build: **0 warnings, 0 errors**
- self-contained win-x64 publish: passed
- portable artifact upload: passed

The #60 artifact is superseded by this validation-record commit and is not the release artifact. Final PR and merged-main workflows must pass before delivery.

## Remaining limitations

- ACE/ACI state is not stored/validated; generated opening is editable convention only
- coached/not-coached state is not stored and is not invented in Handoff
- unmatched BOL cannot become driver-owned until exact durable Driver Code exists
- no manual/fuzzy BOL assignment
- no emailing/transmitting BOL/documents
- no automatic calls/messages/contact
- no OCR/image recognition/document storage/uploads/attachments
- no BOL analytics/revenue dashboard
- no escalation/routing/approval engine
- Maintenance and DOT are unimplemented separate evaluations
- no destructive work-entry deletion
- no full corrective idle-event editing/audit UI
- no dedicated Driver Leader filter
- no measured representative low-end office-PC benchmark outside GitHub-hosted validation
