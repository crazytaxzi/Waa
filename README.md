# WAA — Work Accountability Assistant

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, reviewing Missing BOL orders, and producing an editable end-of-shift handoff from saved history.

The application is driver-centric: **Driver Code** is durable identity. **Driver Name** is display identity. Unit Code and Driver Leader are operational/historical context and never define driver identity.

## Current release line: v0.4.3

v0.4 introduced the centralized one-window workspace and theme-safe text. v0.4.1 fixed the WPF inline `Run.Text` startup-binding issue. v0.4.2 changed generated Handoff presentation to a compact driver-grouped format. v0.4.3 is a presentation-only refinement: a denser Fleet Queue and a centralized gunmetal/neon-purple/neon-green stream palette. It does not change the database schema or validated report/work/BOL business rules.

Current UI/workflow highlights:

- one native WPF `MainWindow` is the permanent operations shell
- full-width Fleet Queue instead of an always-open split driver pane
- single-click or keyboard `Enter` opens Driver Workspace by durable Driver Code
- native Up/Down navigation remains in the Fleet Queue
- Fleet Driver / Unit is compact: Driver Name, then `DriverCode • Unit ######`; Leader remains in its dedicated column
- no separate per-row `Open` column; the whole row is the click target
- tighter virtualized Fleet Queue rows, metrics, and shell spacing fit more drivers in the same viewport
- actionable Idle, Missing BOL, and manual work rows open focused same-window task workspaces
- Back/breadcrumb navigation returns to actual prior context
- safe `Alt+Left` Back outside text-editing controls
- `Next Work Item` uses one deterministic within-driver order, then existing `Next Needing Attention`
- Handoff and Unmatched Missing BOL are full-width same-window workspaces
- edited Handoff text, New Work drafts, BOL notes, queue search, and selection survive in-session navigation
- report updates rebuild valid routes by stable IDs and fail gracefully when an entity disappears
- centralized Light/Dark palettes and automatic theme-safe ordinary/generated text
- v0.4.3 dark mode uses gunmetal surfaces with restrained neon purple and neon green semantic accents
- deterministic source-audit and contrast tests block inappropriate fixed UI colors

The old giant selected-driver split pane is removed rather than retained in parallel.

## Operational capabilities preserved

- searchable, virtualized current-driver fleet queue
- weighted driver/fleet 7-day idle
- weighted driver/fleet complete-coverage 28-day idle
- configurable idle threshold, default 50%, strict greater-than comparison
- current-cycle `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up`
- one automatic report update during launch and explicit `Update Reports` afterward
- managed read-only `Order Details Missing BOL*.xlsx` ingestion without Excel/Office
- exact Driver Code BOL matching, with unmatched source codes visible and read-only
- aggregate unresolved BOL and Open Work counts in Fleet Queue
- exactly one linked task for each matched unresolved Missing BOL item
- Requested, Attempted, Follow-up, Resolved, and Reopen BOL actions
- manual work as `Done`, `Waiting`, or `Follow-up`
- unresolved carry-forward until explicit Resolve
- Reopen without deleting original history
- atomic idle-contact/linked-work and BOL item/task/action/activity writes
- Today’s Activity
- deterministic editable Handoff and Copy to Clipboard
- persisted Light/Dark preference
- SQLite under `%LOCALAPPDATA%\WAA`
- self-contained Windows x64 portable publishing with no installer or administrator requirement

## Daily workflow

1. Start `WAA.exe`. WAA opens the existing local database and checks Downloads once for current Rolling 7 Day and Missing BOL reports.
2. Work the full-width Fleet Queue. Search or use existing priority order, then click a driver/unit row or focus it and press `Enter`.
3. In Driver Workspace, review summary context and `NEEDS ATTENTION`. Each actionable object appears once.
4. Open one focused task at a time: Idle, Missing BOL, or manual work. Save, Back, or choose `Next Work Item`.
5. Use `Add Work` for Done/Waiting/Follow-up notes. A successful save returns to the same Driver Workspace.
6. Use `Next Needing Attention` to advance through the current visible/search-filtered queue.
7. Open Handoff from the shell. Edit as needed and choose `Copy to Clipboard`. Navigating away/back preserves the draft unless `Regenerate` is intentionally pressed.

## Central workspace routes

The shell hosts focused routes through one central `ContentControl`:

- Fleet Queue
- Driver Workspace
- Idle Task
- Missing BOL Task
- Work Item Task
- New Work
- Activity Detail (read-only)
- Handoff
- Unmatched Missing BOL (read-only)
- Unavailable state for stale entities after refresh

Driver-owned navigation is keyed by durable Driver Code, never Unit Code or Driver Leader. Task routes use persisted item/work IDs. Fresh launch always starts at Fleet Queue; deep routes are not restart-persisted.

See [`docs/CENTRAL_WORKSPACE.md`](docs/CENTRAL_WORKSPACE.md).

## Compact Handoff in v0.4.2+

The generated Handoff is intentionally an operational turnover note rather than a verbose database report.

It begins with the editable line:

`No open ACE/ACI's`

WAA **does not currently model or verify ACE/ACI state**. This opening is a user-requested handoff convention. If it is not true for the shift, edit it before copying the handoff.

Next, WAA produces at most one narrative line per driver, alphabetically by Driver Name. It combines relevant unresolved work and current-day activity into that driver line. Current fleet Unit Code and Driver Name are preferred when available.

Idle activity keeps the useful human note and a concise action phrase instead of repeating 28-day/7-day metric boilerplate. The underlying saved metric snapshots remain intact in WAA.

Missing BOL action activity prefers the human-entered note when present. WAA does not invent coaching status or other facts that are not stored.

The draft then contains:

`Missing BOLs:`

Each driver appears once in that section. All unresolved matched BOL order numbers for the driver are grouped onto one line, for example:

`242163 — Brad Example [ABC123]: Missing BOL for orders AST2543, ASU1575`

The compact Handoff deliberately omits Empty Call Date, route, and local BOL status from that copied section; those details remain available in the focused Missing BOL workspace.

The old visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` headings are no longer used by the runtime draft. The underlying open/completed/local-day classification remains deterministic and regression-tested.

See [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md).

## Automatic Light/Dark text and v0.4.3 stream palette

WAA has explicit Light and Dark modes. “Auto” text means ordinary controls inherit the active palette automatically; it does not add an OS/System appearance option.

Theme ownership is centralized in:

- `Themes/LightColors.xaml`
- `Themes/DarkColors.xaml`
- `Themes/BaseStyles.xaml`

`ThemeManager` swaps only the active palette. Styles use `DynamicResource`, so the visible fleet, driver/task workspaces, Handoff, inputs, DataGrid generated text, selected/disabled/hover/focus states, tooltips, and semantic text update live without restart or route reset.

In v0.4.3, dark mode uses gunmetal app/panel/header surfaces. Purple is the primary semantic accent for selection, focus, breadcrumbs, Handoff, and highlighted actions. Green is the positive semantic accent for completed state and `Next Needing Attention`. Ordinary text remains neutral and readable; there is no glow, blur, gradient, or decorative animation. Light mode preserves the same semantic purple/green roles on light neutral surfaces.

Theme preference remains local SQLite. Preference writes occur off the UI thread and a failed write restores the prior visible theme.

See [`docs/THEMING.md`](docs/THEMING.md).

## Queue priority and search

The queue retains four deterministic bands:

1. Above threshold with unfinished idle contact: Spoke — Follow-up, Attempted, then Not Contacted.
2. Above threshold with Spoke; unresolved work before clear drivers.
3. Remaining drivers with unresolved work, including Missing BOL tasks.
4. Remaining clear drivers.

Search matches Driver Code, Driver Name, Unit Code, Driver Leader, and attached Order # text through deterministic substring search. `Next Needing Attention` considers only visible results.

Within a driver, `Next Work Item` uses:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual Follow-up
4. manual Waiting
5. other supported unresolved manual work

When no next item exists, it falls through to existing visible-queue `Next Needing Attention` behavior.

## Missing BOL source and matching

Expected workbook family:

`Order Details Missing BOL*.xlsx`

Temporary Office lock files beginning with `~$` are ignored. WAA reads XLSX locally through a bounded managed ZIP/XML parser and requires no Excel, Office, COM automation, Internet access, or administrator rights.

`Order #` is the durable BOL item key. `Last Dispatch Driver cd` matches only by trimmed uppercase-invariant exact text to durable WAA Driver Code. Codes remain text so leading zeros are preserved. `Last Dispatch Driver nm` is evidence only and never replaces durable Driver Name.

There is no name, Unit Code, truck, leader, substring, prefix, similarity, fuzzy, or probabilistic matching. Blank/unknown source codes remain in the read-only Unmatched Missing BOL workspace and create no driver-owned task. If a later Rolling 7 Day import introduces the exact durable code, WAA attaches the item and creates its task exactly once.

Disappearance from a later workbook never resolves local work. A resolved item that appears again remains resolved and can be explicitly reopened. Reopen reuses the same linked task. BOL actions append history and save synchronized item/task/action/activity state atomically.

See [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md).

## Work log

Waiting and Follow-up remain unresolved across launches/report cycles until explicitly resolved. Resolve/Reopen preserves original text, status, creation time, and Unit/Leader/report-cycle snapshots.

Idle contacts create one linked work entry in the same transaction. Missing BOL tasks/actions use synchronized links and cannot be generically resolved in a way that drifts BOL item state.

## Report refresh behavior

Reports update only:

1. once automatically during application launch
2. when the user explicitly chooses `Update Reports`

There is no report watcher, polling timer, recurring scan, or automatic mid-session refresh. Rolling 7 Day and Missing BOL import independently, and invalid sources retain last-known-good state for that source.

While navigated below Fleet Queue, WAA records route/stable IDs, refreshes repository state, then rebuilds the current workspace when possible. Unsaved New Work/BOL note drafts remain in session. If the current entity no longer exists, an `Unavailable` workspace provides a safe route back.

## Weighted idle rules

- Driver 7-day = current idle hours / current engine hours × 100.
- Driver 28-day = summed idle hours / summed engine hours for current, -7, -14, and -21 day expected periods.
- Fleet values also use summed numerator/denominator calculations.
- Complete driver 28-day requires all four expected observations.
- Missing coverage is displayed rather than inventing a percentage.
- Zero denominator displays `N/A`.

WAA never averages weekly percentages to produce a 28-day result.

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

Application files and user data are separate. v0.4.3 requires no new database schema migration. Replacing the portable folder leaves roster, observations, contacts, work history, BOL state/actions, threshold, and appearance preference under `%LOCALAPPDATA%\WAA`.

## Permanent exclusions

WAA deliberately does not add:

- emailing/transmitting documents
- automatic calls/messages/driver contact
- OCR or image recognition
- document upload/storage/attachment management
- giant BOL dashboards, analytics, or financial/revenue portals
- fuzzy/name/unit/truck/leader/probabilistic identity matching
- complex escalation trees, routing engines, or approval workflows
- browser UI, WebView, local HTTP server, Node, cloud services, or helper processes
- background report polling/watchers

No placeholder architecture is maintained for those capabilities.

## Privacy

This repository is public. Source/tests use synthetic identities, orders, routes, customers, reports, and databases only. Never commit production employee/company data, copied production reports/databases, screenshots, or logs containing operational identities.

## Documentation

- [`docs/CENTRAL_WORKSPACE.md`](docs/CENTRAL_WORKSPACE.md) — one-window navigation, state, keyboard, and performance contract
- [`docs/THEMING.md`](docs/THEMING.md) — Auto text, stream palette, semantic colors, contrast, and source audit
- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — report contracts, precedence, and weighted calculations
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md) — current-cycle idle accountability and queue ordering
- [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md) — authoritative Missing BOL workflow
- [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md) — authoritative work-log and compact Handoff specification
- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — implemented milestones and future boundaries
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — exact implementation/validation state
