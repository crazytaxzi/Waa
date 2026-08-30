# WAA — Work Accountability Assistant

WAA is a local, portable Windows application for working through a driver fleet, recording what happened, carrying unresolved work forward, reviewing Missing BOL orders, and producing an editable end-of-shift handoff from saved history.

The application is driver-centric: **Driver Code** is the durable key, **Driver Name** is display identity, and Unit Code and Driver Leader are operational/historical context rather than identity.

## Current milestone: Central Workspace + Theme-Safe Text v0.4

v0.4 keeps the validated v0.3 data/workflow rules and changes how the application is worked:

- one native WPF `MainWindow` is the permanent operations shell
- Fleet Queue uses the central width instead of sharing space with an always-open driver pane
- single-click or keyboard `Enter` on a driver row opens a full Driver Workspace in the same window
- clicking an actionable idle, Missing BOL, or manual work row opens one focused task workspace
- Back/breadcrumb navigation returns to the actual prior driver/task context
- `Alt+Left` performs safe Back navigation when focus is not in a text-editing control
- `Next Work Item` moves through one driver’s deterministic work order, then reuses `Next Needing Attention`
- Handoff and Unmatched Missing BOL are full-width same-window workspaces
- edited Handoff text, New Work drafts, BOL notes, queue search, and selection are preserved through in-session navigation
- report updates rebuild a valid current route by stable IDs and fail gracefully when a driver/task is no longer available
- Light/Dark mode uses centralized palette dictionaries and automatic theme-safe ordinary text
- DataGrid-generated text, selected rows, disabled text, semantic text, inputs, tooltips, buttons, hover/focus states, and editors use theme resources rather than Windows/default fixed colors
- deterministic source-audit and contrast tests block inappropriate hard-coded UI colors

The old giant selected-driver split pane is removed rather than hidden in parallel.

## Existing operational capabilities preserved

- searchable, virtualized current-driver fleet queue
- weighted driver/fleet 7-day idle
- weighted driver/fleet complete-coverage 28-day idle
- configurable idle threshold, default 50%, strict greater-than comparison
- current-cycle `Not Contacted`, `Attempted`, `Spoke`, and `Spoke — Follow-up`
- one automatic report update during launch and explicit `Update Reports` afterward
- managed read-only `Order Details Missing BOL*.xlsx` ingestion without Excel/Office
- exact Driver Code BOL matching, with unmatched source codes visible and read-only
- compact unresolved BOL and Open Work counts in the fleet queue
- one linked task for each matched unresolved Missing BOL item
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
2. Work the full-width Fleet Queue. Search or use the existing priority order, then single-click a driver/unit row (or focus it and press `Enter`).
3. In Driver Workspace, review the summary and `NEEDS ATTENTION` list. Each actionable object appears once.
4. Open one focused task at a time: Idle, Missing BOL, or manual work. Save the action, use Back, or choose `Next Work Item`.
5. Use `Add Work` for ordinary Done/Waiting/Follow-up notes. A successful save returns to the same Driver Workspace with the new work kept in context.
6. Use `Next Needing Attention` when the current driver has no more relevant work or you want to advance through the current visible/search-filtered queue.
7. Open Handoff from the shell. Edit as needed and use `Copy to Clipboard`. Navigating away/back keeps the draft unless `Regenerate` is intentionally pressed.

## Central workspace routes

The shell hosts these focused routes through one `ContentControl`:

- Fleet Queue
- Driver Workspace
- Idle Task
- Missing BOL Task
- Work Item Task
- New Work
- Activity Detail (read-only)
- Handoff
- Unmatched Missing BOL (read-only)
- Unavailable state for a stale entity after refresh

Driver-owned navigation is keyed by durable Driver Code, never Unit Code or Driver Leader. Task routes use persisted item/work IDs. A fresh launch always starts at Fleet Queue; deep routes are not persisted across restart.

See [`docs/CENTRAL_WORKSPACE.md`](docs/CENTRAL_WORKSPACE.md) for the complete navigation/state/accessibility contract.

## Automatic Light/Dark text

WAA has explicit Light and Dark modes; “Auto” text means ordinary controls inherit the active palette automatically. It does not add an OS/System theme mode.

Theme ownership is centralized in:

- `Themes/LightColors.xaml`
- `Themes/DarkColors.xaml`
- `Themes/BaseStyles.xaml`

`ThemeManager` swaps only the palette dictionary. Styles use `DynamicResource`, so the visible fleet, driver/task pages, Handoff, inputs, DataGrid rows/headers/generated text, selected state, disabled state, hover/focus state, tooltips, and semantic warning/follow-up/completed/information text update live without restart or route reset.

Theme preference persistence remains local SQLite. The preference write is performed off the UI thread; if saving it fails, WAA restores the prior visible theme.

Repository-level tests enforce matching palette keys, dynamic theme usage, prohibited-color auditing, and deterministic contrast thresholds. See [`docs/THEMING.md`](docs/THEMING.md).

## Queue priority and search

The queue retains the existing four deterministic bands:

1. Above threshold with unfinished idle contact: Spoke — Follow-up, Attempted, then Not Contacted.
2. Above threshold with Spoke; unresolved work before clear drivers.
3. Remaining drivers with unresolved work, including Missing BOL tasks.
4. Remaining clear drivers.

Within unfinished high-idle work, the largest valid current idle concern and stable name/code tie-breakers remain authoritative. Within otherwise equal ordinary unresolved work, an older open Missing BOL Empty Call Date may break the tie.

Search matches Driver Code, Driver Name, Unit Code, Driver Leader, and attached Order # text through deterministic substring search. `Next Needing Attention` considers only visible results.

`Next Work Item` does not create another fleet priority engine. Within the current driver it uses:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual Follow-up
4. manual Waiting
5. other supported unresolved manual work

When no next item exists, it falls through to existing visible-queue `Next Needing Attention` behavior.

## Missing BOL source and matching

Expected workbook family:

`Order Details Missing BOL*.xlsx`

Temporary Office lock files beginning with `~$` are ignored. WAA reads XLSX locally through its bounded managed ZIP/XML parser; it does not require Excel, Office, COM automation, Internet access, or administrator rights.

`Order #` is the durable BOL item key. `Last Dispatch Driver cd` matches only by trimmed uppercase-invariant exact text to durable WAA Driver Code. Codes remain text so leading zeros are preserved. `Last Dispatch Driver nm` is evidence only and never replaces durable Driver Name.

There is no name, Unit Code, truck, leader, substring, prefix, similarity, fuzzy, or probabilistic matching. Blank/unknown source codes remain in the read-only Unmatched Missing BOL workspace and create no driver-owned task. If a later Rolling 7 Day import introduces the exact durable code, WAA attaches the item and creates its task exactly once.

For matched work, disappearance from a later workbook never resolves local state. A resolved item that appears again remains resolved and can be explicitly reopened. Reopen reuses the same linked task. Every BOL action appends history and saves item/task/action/activity state atomically.

See [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md).

## Work log and Handoff

Waiting and Follow-up work remains unresolved across launches and report cycles until explicitly resolved. Resolve/Reopen preserves original text, status, creation time, and Unit/Leader/report-cycle snapshots.

Idle contacts create one linked work entry in the same transaction. Missing BOL tasks/actions use their existing synchronized links and cannot be resolved through a generic path that would drift BOL item state.

Handoff is generated from saved work using the PC’s local calendar-day boundary and always contains:

- `NEEDS FOLLOW-UP`
- `WAITING / PENDING`
- `COMPLETED TODAY`

Editing/copying the Handoff draft never mutates reports, work history, BOL state, contacts, threshold, or identity. `Regenerate` intentionally rebuilds it from current saved records.

See [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md).

## Report refresh behavior

Reports update only:

1. once automatically during application launch
2. when the user explicitly chooses `Update Reports`

There is no report watcher, polling timer, recurring scan, or automatic mid-session refresh. Rolling 7 Day and Missing BOL import independently. A bad/locked/conflicting source retains last-known-good state for that source.

While navigated below Fleet Queue, WAA records the route and stable IDs, refreshes repository state, then rebuilds the current workspace when possible. New Work and BOL note drafts are retained. If the current entity no longer exists, an `Unavailable` workspace provides a safe route back rather than crashing or silently discarding typed text.

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

The application folder and data folder are separate. v0.4 navigation/theming requires no database schema migration, and replacing the portable application folder leaves existing roster, observations, contacts, work history, BOL state/action history, threshold, and appearance preference under `%LOCALAPPDATA%\WAA`.

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

## Technical shape

- .NET 8 native WPF
- one top-level `MainWindow`
- focused UserControls/DataTemplates in one central ContentControl
- `Microsoft.Data.Sqlite`
- managed ZIP/XML XLSX reader
- one desktop process
- local persistence only
- indexed aggregate fleet/work/BOL reads
- queue virtualization/recycling
- short transactional writes
- report/database work kept off the UI thread
- no browser stack, local server, Node, cloud service, helper process, watcher, or recurring timer

## Documentation

- [`docs/CENTRAL_WORKSPACE.md`](docs/CENTRAL_WORKSPACE.md) — v0.4 one-window navigation, state, keyboard, and performance contract
- [`docs/THEMING.md`](docs/THEMING.md) — v0.4 Auto text, palettes, semantic colors, contrast, and source audit
- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — report contracts, precedence, and weighted calculations
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md) — current-cycle idle accountability and queue ordering
- [`docs/MISSING_BOL_WORKFLOW.md`](docs/MISSING_BOL_WORKFLOW.md) — authoritative Missing BOL workflow
- [`docs/WORK_LOG_HANDOFF.md`](docs/WORK_LOG_HANDOFF.md) — authoritative work-log and Handoff specification
- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — implemented milestones and future boundaries
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — exact current implementation/validation state
