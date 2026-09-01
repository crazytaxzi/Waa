# WAA — Work Accountability Assistant

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, reviewing the current Missing BOL report, and producing an editable end-of-shift Handoff.

**Driver Code** is durable identity. **Driver Name** is display identity. Unit Code and Driver Leader are operational/historical context and never define driver identity.

## Current release line: v0.4.6

v0.4 introduced the centralized one-window workspace and theme-safe text. v0.4.1 fixed inline `Run.Text` startup binding. v0.4.2 made Handoff compact. v0.4.3 tightened Fleet Queue density and introduced the gunmetal/neon-purple/neon-green stream palette. v0.4.4 grouped Handoff by Driver Leader and fixed the complete dark MainWindow shell. v0.4.5 added bounded ambient motion. v0.4.5.1 restored direct user control of that motion. **v0.4.6 makes Missing BOL a read-only view of the current workbook instead of a persisted WAA workflow.**

Current UI/workflow highlights:

- one native WPF `MainWindow` permanent shell
- searchable, virtualized full-width Fleet Queue
- row click or keyboard `Enter` opens Driver Workspace by durable Driver Code
- native Up/Down DataGrid navigation remains
- compact Fleet identity: Driver Name, then `DriverCode • Unit ######`; Leader stays in its own column
- focused same-window Idle/manual work task pages
- dedicated read-only `CURRENT MISSING BOL` section inside Driver Workspace
- read-only same-window Missing BOL order detail and Unmatched Missing BOL workspace
- Back/breadcrumb state preservation and safe `Alt+Left`
- deterministic `Next Work Item` for actual work only and search-respecting `Next Needing Attention`
- full-width Handoff workspace
- Driver Leader-grouped Handoff narrative and current-file Missing BOL section
- centralized Light/Dark palettes with automatic theme-safe text
- complete dark-mode MainWindow/client background
- optional dark-mode ambient scanline + sparse electric-blue dust
- user-controlled Ambient Motion preference even on reduced-animation Windows sessions
- restrained button hover/press motion

## v0.4.6 source-only Missing BOL

Expected workbook family: `Order Details Missing BOL*.xlsx` in Windows Downloads.

The workbook is now the Missing BOL source of truth. WAA parses the accepted workbook into memory and shows what is currently in that file. Missing BOL rows, source hashes, local status, actions, notes, resolution state, action history, and generated BOL tasks are **not written to SQLite**.

That means:

- a current matched row appears under that driver in `CURRENT MISSING BOL`
- a current unmatched row appears in the read-only Unmatched Missing BOL view
- deleting/removing a row from the source workbook makes it disappear after the next launch/manual report update
- restarting WAA does not restore Missing BOL rows from the database; the file is scanned again
- Missing BOL does not increase Open Work or driver priority
- Missing BOL does not enter `Next Work Item` or Today’s Activity
- there are no Requested / Attempted / Follow-up / Resolved / Reopen BOL controls anymore
- Handoff still includes a compact Missing BOL section, generated transiently from the current workbook when Handoff is regenerated

Matching remains intentionally strict: `Last Dispatch Driver cd` matches only trimmed uppercase-invariant exact text to a **current durable Driver Code**. Codes remain text, preserving leading zeros. There is no name, Unit, Leader, truck, substring, prefix, similarity, fuzzy, probabilistic, or manual matching.

Older v0.3–v0.4.5 databases may physically contain old `missing_bol_*` tables and generated BOL work. v0.4.6 does not destructively delete or rewrite them during upgrade, but they are dormant: they do not repopulate the current report, do not count as current Open Work, do not appear in Today’s Activity, and do not drive current Handoff/priority.

See [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md).

## Ambient motion

Ambient motion remains intentionally subtle and bounded:

- one faint rolling scanline
- eight sparse 2–3 pixel electric-blue motes
- very low opacity and slow movement
- non-interactive overlay (`IsHitTestVisible=False`)
- no timer, particle engine, blur, glow, shader, background worker, browser surface, or new graphics dependency

The ambient effect runs only in Dark mode while the current WAA Ambient Motion preference is enabled. If no WAA motion preference has ever been saved, Windows `SystemParameters.ClientAreaAnimation` supplies the initial default only. The button always remains usable; after the user chooses on/off, the persisted WAA choice wins on later launches including RDP/enterprise-policy/performance-tuned sessions.

The shell control reads `Motion off` when motion is enabled and `Motion on` when disabled. Buttons use only a tiny template-local hover scale (`1.012x`) and slight pressed opacity feedback.

## Operational capabilities

- weighted driver/fleet 7-day idle and complete-coverage 28-day idle
- configurable idle threshold and prioritized fleet queue
- current-cycle idle contact outcomes and linked saved work
- one automatic report scan at launch plus explicit `Update Reports`
- managed read-only `Order Details Missing BOL*.xlsx` parsing without Excel/Office
- normalized exact current Driver Code BOL matching with unmatched source rows visible/read-only
- manual Done/Waiting/Follow-up work and unresolved carry-forward
- Resolve/Reopen for saved manual/idle-linked work without deleting original history
- Today’s Activity from current saved work activity
- deterministic editable Handoff and Copy to Clipboard
- persisted Light/Dark and explicit Ambient Motion preferences
- SQLite under `%LOCALAPPDATA%\WAA`
- self-contained Windows x64 portable deployment with no installer/admin requirement

## Daily workflow

1. Start `WAA.exe`; WAA opens saved work/roster state and checks Downloads once for current reports.
2. Work the Fleet Queue; click a driver row or focus it and press `Enter`.
3. Review `NEEDS ATTENTION` for actual work and `CURRENT MISSING BOL` for read-only current report rows.
4. Open focused Idle/manual work tasks as needed; Missing BOL order detail is informational only.
5. Save actual work and return to the same driver context.
6. Use `Next Work Item` / `Next Needing Attention` to advance through actionable work.
7. Open Handoff, edit as needed, and Copy to Clipboard. The draft survives navigation until explicit `Regenerate`.

## Handoff

The runtime Handoff begins with the editable convention:

`No open ACE/ACI's`

WAA does **not** model or verify ACE/ACI state; edit the line if it is not true.

Represented drivers are grouped under headings such as:

`Driver Leader: LEADER-A`

Leader headings sort alphabetically; drivers within each leader sort by Driver Name then Driver Code. Saved non-BOL work uses current fleet identity when available with historical snapshot fallback. The dedicated Missing BOL section is rebuilt from **current matched workbook rows** and uses current driver/leader context. Each represented driver appears once in that BOL section with current Order # values compactly grouped.

See [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md).

## Theme system

Theme ownership remains centralized in:

- `Themes/LightColors.xaml`
- `Themes/DarkColors.xaml`
- `Themes/BaseStyles.xaml`

Dark mode uses gunmetal surfaces, purple selection/focus/breadcrumb/Handoff accents, green positive/completed/`Next Needing Attention` accents, and neutral readable body text. Ambient decoration uses dedicated palette resources and never carries status meaning.

`ThemeManager` swaps only the palette. Dynamic resources update the complete client shell and visible workspaces without restart or navigation reset.

See [`docs/THEMING.md`](docs/THEMING.md).

## Report refresh behavior

Reports are scanned only:

1. once during launch
2. when the user explicitly chooses `Update Reports`

There is no report watcher, polling timer, recurring scan, or automatic mid-session refresh.

Rolling 7 Day remains saved/imported state. Missing BOL is current-session source-only state from the accepted workbook.

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

v0.4.6 requires no destructive database migration. Existing roster, observations, contacts, saved work history, threshold, Handoff draft/session behavior, Light/Dark preference, and Ambient Motion preference remain. Legacy BOL database artifacts are left untouched but are no longer current product state.

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