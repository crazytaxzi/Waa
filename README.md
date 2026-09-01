# WAA — Work Accountability Assistant

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, reviewing Missing BOL orders, and producing an editable end-of-shift Handoff from saved history.

**Driver Code** is durable identity. **Driver Name** is display identity. Unit Code and Driver Leader are operational/historical context and never define driver identity.

## Current release line: v0.4.5.1

v0.4 introduced the centralized one-window workspace and theme-safe text. v0.4.1 fixed inline `Run.Text` startup binding. v0.4.2 made Handoff compact. v0.4.3 tightened Fleet Queue density and introduced the gunmetal/neon-purple/neon-green stream palette. v0.4.4 grouped Handoff by Driver Leader and fixed the complete dark MainWindow shell. v0.4.5 added a deliberately light ambient-motion layer and restrained button feedback. v0.4.5.1 keeps that motion control user-operated even when Windows reports reduced client animation.

Current UI/workflow highlights:

- one native WPF `MainWindow` permanent shell
- searchable, virtualized full-width Fleet Queue
- row click or keyboard `Enter` opens Driver Workspace by durable Driver Code
- native Up/Down DataGrid navigation remains
- compact Fleet identity: Driver Name, then `DriverCode • Unit ######`; Leader stays in its own column
- focused same-window Idle, Missing BOL, and manual work task pages
- Back/breadcrumb state preservation and safe `Alt+Left`
- deterministic `Next Work Item` and search-respecting `Next Needing Attention`
- full-width Handoff and Unmatched Missing BOL workspaces
- Driver Leader-grouped Handoff narrative and Missing BOL sections
- centralized Light/Dark palettes with automatic theme-safe text
- complete dark-mode MainWindow/client background
- optional dark-mode ambient scanline + sparse electric-blue dust
- user-controlled Ambient Motion preference even on reduced-animation Windows sessions
- restrained button hover/press motion

## v0.4.5 ambient motion + v0.4.5.1 control hotfix

Ambient motion is intentionally subtle and bounded:

- one faint rolling scanline
- eight sparse 2–3 pixel electric-blue motes
- very low opacity and slow movement
- non-interactive overlay (`IsHitTestVisible=False`)
- no timer, particle engine, blur, glow, shader, background worker, browser surface, or new graphics dependency

The ambient effect itself runs only when:

1. Dark mode is active
2. the current WAA Ambient Motion preference is enabled

If no WAA motion preference has ever been saved, Windows `SystemParameters.ClientAreaAnimation` supplies the initial default only. Windows animation enabled starts WAA motion enabled; Windows animation disabled starts WAA motion disabled. The button stays usable in both cases.

The shell control reads:

- `Motion off` — WAA motion is enabled; click to disable it
- `Motion on` — WAA motion is disabled; click to enable it

There is no greyed-out `Motion reduced` lockout in v0.4.5.1. Once the user clicks the control, WAA stores an explicit `on` or `off` value in the existing SQLite `settings` table and that WAA choice wins on later launches, including RDP, enterprise-policy, or performance-tuned Windows sessions. No database migration/schema version change is required.

Buttons use only a tiny template-local hover scale (`1.012x`) and slight pressed opacity feedback. Layout, commands, click targets, keyboard accessibility, focus, and semantic colors are unchanged.

## Operational capabilities preserved

- weighted driver/fleet 7-day idle and complete-coverage 28-day idle
- configurable idle threshold and prioritized fleet queue
- current-cycle idle contact outcomes and linked work
- one automatic report update at launch plus explicit `Update Reports`
- read-only managed `Order Details Missing BOL*.xlsx` ingestion without Excel/Office
- normalized exact Driver Code BOL matching; unmatched source codes remain visible/read-only
- one linked task per matched unresolved Missing BOL item
- Requested, Attempted, Follow-up, Resolved, and Reopen BOL actions
- manual Done/Waiting/Follow-up work and unresolved carry-forward
- Resolve/Reopen without deleting original history
- Today’s Activity
- deterministic editable Handoff and Copy to Clipboard
- persisted Light/Dark and explicit Ambient Motion preferences
- SQLite under `%LOCALAPPDATA%\WAA`
- self-contained Windows x64 portable deployment with no installer/admin requirement

## Daily workflow

1. Start `WAA.exe`; WAA opens local state and checks Downloads once for current reports.
2. Work the Fleet Queue; click a driver row or focus it and press `Enter`.
3. Review Driver Workspace and `NEEDS ATTENTION`.
4. Open one focused Idle, Missing BOL, or manual work task at a time.
5. Save work and return to the same driver context.
6. Use `Next Needing Attention` to advance through the visible/search-filtered queue.
7. Open Handoff, edit as needed, and Copy to Clipboard. The draft survives navigation until explicit `Regenerate`.

## Handoff

The runtime Handoff begins with the editable convention:

`No open ACE/ACI's`

WAA does **not** model or verify ACE/ACI state; edit the line if it is not true.

Represented drivers are grouped under headings such as:

`Driver Leader: LEADER-A`

Leader headings sort alphabetically; drivers within each leader sort by Driver Name then Driver Code. Current fleet Driver Leader is preferred, with saved historical leader snapshot as fallback. Missing BOL drivers use the same grouping. Each driver appears once per section and unresolved BOL order numbers are compactly grouped on that line.

See [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md).

## Theme system

Theme ownership remains centralized in:

- `Themes/LightColors.xaml`
- `Themes/DarkColors.xaml`
- `Themes/BaseStyles.xaml`

Dark mode uses gunmetal surfaces, purple selection/focus/breadcrumb/Handoff accents, green positive/completed/`Next Needing Attention` accents, and neutral readable body text. Ambient decoration uses dedicated palette resources and never carries status meaning.

`ThemeManager` swaps only the palette. Dynamic resources update the complete client shell and visible workspaces without restart or navigation reset.

See [`docs/THEMING.md`](docs/THEMING.md).

## Missing BOL identity rules

Expected workbook family: `Order Details Missing BOL*.xlsx`.

`Order #` is durable BOL item identity. `Last Dispatch Driver cd` matches only by trimmed uppercase-invariant exact text to durable WAA Driver Code. Codes remain text, preserving leading zeros. There is no name, Unit, Leader, truck, substring, prefix, similarity, fuzzy, or probabilistic matching.

See [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md).

## Report refresh behavior

Reports update only:

1. once during launch
2. when the user explicitly chooses `Update Reports`

There is no report watcher, polling timer, recurring scan, or automatic mid-session refresh.

## Portable installation and upgrade

WAA targets **.NET 8 native WPF** and publishes self-contained for Windows x64.

First install:

1. Extract the complete portable ZIP to a normal local folder.
2. Do not run it from inside the ZIP.
3. Place current supported reports in Windows Downloads when available.
4. Start `WAA.exe`.

Upgrade:

1. Close WAA.
2. Extract the new portable folder.
3. Replace the old application folder with the new one.
4. Retain `%LOCALAPPDATA%\WAA`.
5. Start `WAA.exe`.

v0.4.5.1 requires no schema migration. Replacing application files leaves roster, observations, contacts, work history, BOL state/actions, threshold, Handoff history, Light/Dark preference, and Ambient Motion preference intact.

## Permanent exclusions

WAA deliberately does not add:

- emailing/transmitting documents
- automatic calls/messages/driver contact
- OCR/image recognition
- document upload/storage/attachment management
- giant BOL dashboards or financial/revenue portals
- fuzzy/name/unit/truck/leader/probabilistic identity matching
- complex escalation/routing/approval engines
- browser UI, WebView, local HTTP server, Node, cloud services, or helper processes
- background report polling/watchers
- heavy animation, blur, glow, shader effects, particle engines, or gamification beyond the bounded v0.4.5 ambient layer

## Privacy

This repository is public. Source/tests use synthetic identities/data only. Never commit production employee/company reports, databases, screenshots, or operational logs.

## Documentation

- [`docs/CENTRAL_WORKSPACE.md`](docs/CENTRAL_WORKSPACE.md)
- [`docs/THEMING.md`](docs/THEMING.md)
- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md)
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md)
- [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md)
- [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md)
- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md)
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md)