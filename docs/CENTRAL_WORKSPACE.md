# WAA Central Workspace v0.4

## Purpose

WAA v0.4 uses one native WPF `MainWindow` as the complete operational shell. The prior always-visible fleet/selected-driver split pane is removed. Work now follows a focused click-through path inside one central content host:

`Fleet Queue → Driver Workspace → Focused Task Workspace`

Handoff and Unmatched Missing BOL are also focused routes in the same window. WAA does not create driver, BOL, work-item, or handoff operating-system windows and does not use a browser, WebView, or navigation framework.

## Shell

`MainWindow` remains visible for the life of the application and owns the persistent shell:

- WAA title and current roster/report summary
- Light/Dark mode action
- `Update Reports`
- `Handoff`
- breadcrumb and Back action below the queue level
- one `ContentControl` bound to the active workspace
- persistent status/progress area

The content host displays one workspace at a time. Route state lives in focused view-models plus the small `WorkspaceNavigator`; business rules remain in the existing repositories and workflow view-models.

## Route hierarchy

Current routes are:

- `FleetQueue`
- `DriverWorkspace`
- `IdleTask`
- `MissingBolTask`
- `WorkItemTask`
- `NewWork`
- `ActivityDetail`
- `Handoff`
- `UnmatchedBol`
- `Unavailable` for a stale entity that cannot be rebuilt safely

Driver-owned routes are keyed by durable Driver Code. Missing BOL tasks use the persisted Missing BOL item ID and manual work/activity routes use the persisted work-entry ID. Unit Code and Driver Leader are context only and never route identity.

A fresh application launch always starts at Fleet Queue. Deep routes are session state, not restart state.

## Fleet Queue

Fleet Queue uses the central workspace width and retains the v0.3 queue contract:

- deterministic search by Driver Code, Driver Name, Unit Code, Driver Leader, and attached Order # text
- configurable idle threshold
- weighted fleet 28-day and 7-day values
- current report cycle and queue progress
- BOL count
- Open Work count
- idle/contact status
- current deterministic priority order
- `Next Needing Attention`
- virtualized/recycling DataGrid rows

A row is one restrained interactive target. A single mouse click opens its Driver Workspace. Keyboard focus plus `Enter` opens the same route. Up/Down remains normal DataGrid navigation. The UI does not require double-click.

The compact BOL/Open Work columns remain summaries; the Driver Workspace provides explicit section actions for focused work. Reliable full-row opening takes precedence over fragile per-cell click routing.

## Driver Workspace

The Driver Workspace is a full central page for one durable Driver Code. Its header shows:

- Driver Name
- Driver Code
- Unit Code
- Driver Leader
- report cycle
- weighted 28-day idle and coverage presentation
- weighted 7-day idle
- current idle-contact state
- Open Work count
- unresolved Missing BOL count

The page is a work index, not a wall of editors.

### Needs Attention

`NEEDS ATTENTION` presents each actionable object once as a compact keyboard-accessible row:

1. unfinished/current idle contact work
2. each unresolved Missing BOL item
3. each unresolved manual Waiting/Follow-up item

The linked idle work entry is represented by the idle item rather than duplicated as manual work. A linked Missing BOL task is represented by its Missing BOL item rather than duplicated as manual work. Existing repository links remain the source of truth.

Each row shows type, concise title, status, important context/date, and an open affordance. Clicking or activating a row opens one focused task workspace.

### Quick Actions

- `Add Work`
- `Next Work Item`
- `Missing BOL` focus
- `Open Work` focus
- `Next Needing Attention`

### Today's Activity

Today’s Activity remains compact and uses the PC local calendar-day boundary already defined by the work-log workflow. Activity rows may open `ActivityDetail`, which is read-only and adds no edit or delete behavior.

When nothing needs attention, the page states:

`No work currently needs attention for this driver.`

## Focused task workspaces

### Idle Task

Idle Task shows Driver Code identity plus Unit/Leader context, report cycle, weighted 28-day value/coverage, weighted 7-day value, threshold, current-cycle outcome, prior note, and one optional action note editor.

Actions remain:

- `Spoke`
- `Attempted`
- `Spoke — Follow-up`

The existing atomic idle-event/linked-work transaction is unchanged. Saving refreshes the current task/driver/queue state without silently jumping away. `Next Work Item` and `Next Needing Attention` remain explicit choices.

### Missing BOL Task

One order is opened at a time. The workspace shows:

- Order #
- Empty Call Date
- route
- supported customer and mileage context
- exact source Driver Code and source name evidence
- latest-report presence
- source-name/presence warnings
- current local status
- optional note
- action history

Actions remain `Requested`, `Attempted`, `Follow-up`, `Resolved`, and `Reopen` when allowed by current state. Exact-code identity, one linked task, action history, transaction boundaries, absence-never-resolves behavior, and synchronized Resolve/Reopen behavior are unchanged. No fuzzy or manual assignment is introduced.

### Manual Work Item

Manual work detail shows original status/text, creation time, source, Unit/Leader/report-cycle snapshots, resolution state, and Driver Code. Ordinary Waiting/Follow-up work may Resolve/Reopen. MissingBolTask work still cannot bypass the synchronized BOL workflow.

### New Work

`Add Work` opens one multiline editor with `Done`, `Waiting`, and `Follow-up` actions. Existing whitespace prevention, duplicate-submit protection, retry text retention, transactional save, and context snapshots are preserved.

After a successful save WAA returns to the same Driver Workspace and keeps the saved item highlighted/in context.

### Activity Detail

Activity Detail is read-only. It exposes saved text, status/source, timestamps, Unit/Leader/report-cycle snapshots, and resolution context. It provides no edit/delete mutation path.

## Handoff

Handoff is a central full-width route. It preserves:

- `Back to Queue`
- `Regenerate`
- editable draft
- `Copy to Clipboard`
- deterministic Needs Follow-up / Waiting / Completed Today sections

The first visit generates from saved work. Navigating away and back during the same application session preserves the edited draft. `Regenerate` intentionally replaces it from current saved work. Editing or copying never mutates repository state.

## Unmatched Missing BOL

The queue’s unmatched count opens a central read-only route containing Order #, Empty Call Date, source Driver Code/name, route, latest-report presence, and an explicit exact-match explanation. It provides no fuzzy or manual assignment path.

## Back and breadcrumbs

Routes below Fleet Queue expose a breadcrumb and Back action, for example:

- `Fleet`
- `Fleet > Alex Example`
- `Fleet > Alex Example > Idle`
- `Fleet > Alex Example > Missing BOL > BOL-100`

`WorkspaceNavigator` keeps a real in-session back stack for driver/task navigation. Back from a task returns to the actual prior Driver Workspace. Handoff and Unmatched BOL deliberately return to Fleet Queue.

`Alt+Left` invokes Back when available unless keyboard focus is inside `TextBoxBase`/`PasswordBox`; normal text editing is not hijacked.

## Session-state preservation

Within one running application WAA preserves:

- queue search text
- threshold text/value
- selected Driver Code
- deterministic queue ordering
- selected row and useful scroll position where WPF virtualization permits
- current driver/focus context
- per-driver New Work drafts
- per-order Missing BOL note drafts
- edited Handoff draft

Returning from a task keeps the same driver. Returning to Fleet Queue retains the current search and selected driver.

Deep route state is intentionally not persisted across restart.

## Report update while navigated

`Update Reports` remains globally available. Report/database work remains off the UI thread through the existing update path.

Before refresh, MainViewModel records the current route and selected Driver Code. After reload it rebuilds the route using stable IDs when the entity still exists. Unsaved New Work and Missing BOL note drafts are kept in session state.

If a driver or item cannot be rebuilt, WAA shows a focused `Unavailable` workspace with a clear return path instead of dereferencing stale view-model objects or crashing.

## Next Work Item

For the current driver, actionable work is ordered deterministically:

1. unfinished idle contact
2. unresolved Missing BOL, repository order with oldest Empty Call Date first
3. manual Follow-up, oldest first
4. manual Waiting, oldest first
5. other unresolved manual work if introduced by the existing supported model

`Next Work Item` moves within that already-built list. It does not create another fleet priority engine.

When the current driver has no next open item, WAA reuses existing `Next Needing Attention` logic. That logic follows the current visible/search-filtered queue and preserves the existing fleet priority rules.

## Performance boundaries

v0.4 is a presentation/navigation refactor, not a second data layer.

- Fleet data continues through aggregate/indexed repository reads.
- No one-query-per-driver or one-query-per-row path is added.
- Selected driver work and BOL collections load only for the selected Driver Code/state refresh.
- One Missing BOL action history is loaded only when its focused task opens.
- No timer, report watcher, recurring database polling, or query-per-keystroke path exists.
- Queue virtualization/recycling remains enabled.
- Database/report operations use bounded `Task.Run` work and short existing transactions.
- Hidden legacy split-pane controls and duplicate query paths are removed.

## Keyboard and accessibility

- fleet row: focus + `Enter` opens Driver Workspace
- actionable driver rows: actual `Button` controls, so Enter/Space activation works through WPF keyboard semantics
- visible keyboard focus uses `FocusBorderBrush`
- buttons use explicit automation names where context needs clarification
- Back/breadcrumb/status have clear labels
- status is expressed in text, with semantic color as secondary information
- pointer cursor/hover/focus and restrained chevrons indicate clickable rows
- no mouse-only task action is required

## Window invariant

`MainWindow` is the only top-level WPF Window in the application source. Driver, idle, BOL, work, new-work, activity, handoff, unmatched, and unavailable workspaces are UserControls/DataTemplates hosted in its central ContentControl.

The old split-pane workflow must not be reintroduced in parallel.
