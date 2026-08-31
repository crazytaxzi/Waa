# WAA Implementation Status

## Current bounded release

**WAA v0.4.3 — Fleet Queue Density + Stream Theme Refresh release candidate.**

This release is presentation-only. It changes Fleet Queue density/content presentation and refreshes the centralized Light/Dark palette. It does not change the database schema, report parsing, queue priority rules, work/BOL transactions, Handoff generation rules, route identity, or `%LOCALAPPDATA%\WAA` compatibility.

Current release branch implementation commit: `61e3550d4d75978b6581c6f30370b34a4750a9c6` before documentation alignment.

PR-tree and merged-main Windows validation are required before the v0.4.3 portable artifact is delivered.

Prior validated baseline: **WAA v0.4.2**, merged to `main`, workflow **#62**, run ID `33342993413`, August 30, 2026 — **191 passed, 0 failed, 0 skipped**.

## Runtime and deployment

- .NET 8 native WPF
- one top-level `MainWindow`
- warnings treated as errors
- Windows x64 self-contained portable publish
- no installer/admin/SDK/separate .NET/Excel/Office requirement in published build
- one local desktop process
- SQLite/preferences under `%LOCALAPPDATA%\WAA`
- GitHub Actions restores, builds/WPF-compiles, tests, publishes, and uploads artifact

## v0.4.3 Fleet Queue density

The Fleet Queue remains the full-width primary workspace and keeps the same data/query/priority behavior.

Presentation changes:

- the dedicated `Open` column is removed
- the entire DataGrid row remains the mouse click target for Driver Workspace
- native Up/Down DataGrid navigation remains intact
- `Enter` still opens the focused row
- Driver / Unit renders Driver Name on line one
- Driver / Unit line two is `DriverCode • Unit ######`
- Leader remains visible only in the dedicated `Leader` column in Fleet Queue
- the original richer `IdentityLine` remains available to Driver/task workspaces, so Leader context outside Fleet Queue is preserved
- Fleet Queue uses compact centralized DataGrid row/cell styles with a 36-pixel minimum row height and reduced vertical cell padding
- search/threshold/metric/shell spacing is tightened without reducing ordinary text contrast
- row and column virtualization, recycling, and content scrolling remain enabled

No old split pane, per-row Open button, alternate routing path, or non-virtualized queue was introduced.

## v0.4.3 stream theme

Theme ownership remains centralized in `LightColors.xaml`, `DarkColors.xaml`, and `BaseStyles.xaml`.

Dark-mode visual intent:

- gunmetal app/panel/raised/header surfaces
- neon purple for primary/highlight/focus/selection/breadcrumb/Handoff roles
- neon green for positive/completed/`Next Needing Attention` roles
- neutral light ordinary text
- no glow, blur, gradient, animation, or decorative effects

Light mode retains the same semantic accent roles on light neutral surfaces. Both palettes contain identical required key sets.

Specific UI application:

- Main shell/header uses centralized header surface
- Handoff remains purple primary
- `Next Needing Attention` uses the green success style
- `Update Reports` and appearance toggle remain neutral controls
- breadcrumbs use the purple breadcrumb role
- fleet row hover/selection/focus use centralized resources
- task, Driver Workspace, Handoff, status/editor, DataGrid, and semantic states continue to inherit theme resources

Recommended literal palette values were adapted only where necessary for tested contrast. In particular, boundary colors were raised from the suggested dark border value so actual panel/control edges remain at least 3:1, while the requested purple/green accents remain recognizable.

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
- deterministic contrast tests cover normal/semantic/selected/editor/hover/button/breadcrumb/focus cases
- all data-bound inline WPF `Run.Text` uses explicit one-way display binding
- repository regression test prevents the v0.4 startup binding failure from returning

## v0.4.2 compact Handoff preserved

Runtime generated Handoff remains the compact operational shape introduced in v0.4.2.

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

Edited draft isolation remains unchanged: navigating away/back preserves edit, Regenerate intentionally replaces it, Copy copies current edit, and no Handoff edit mutates saved work/BOL/idle/report/settings state.

## Database compatibility

v0.4.3 requires **no database schema change** and does not increment schema version.

Existing `%LOCALAPPDATA%\WAA` remains compatible and preserves:

- roster/import metadata/observations
- idle contacts
- work entries and linked idle work
- Missing BOL imports/items/actions/work links
- threshold
- Light/Dark preference
- Handoff source history

Replacing the portable application folder leaves the data folder intact.

## Preserved business/data regression requirements

The v0.4.3 workflow must preserve the existing coverage for:

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
- v0.4.2 compact Handoff behavior

No permanent exclusion is changed by v0.4.3.

## Validation gate

Before delivery, the v0.4.3 release must have:

- PR Windows restore/build/WPF compilation success
- all Core/App/integration/navigation/theme/source-audit tests passing
- zero build warnings/errors under the existing warnings-as-errors configuration
- self-contained win-x64 publish success
- portable artifact upload success
- PR merged to `main`
- merged `main` Windows workflow success
- final merged-main portable artifact downloaded and hashed

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
