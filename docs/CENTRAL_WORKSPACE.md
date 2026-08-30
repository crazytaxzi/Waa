# WAA Central Workspace v0.4.2

## Purpose

WAA uses one native WPF `MainWindow` as the complete operational shell. The old always-visible fleet/selected-driver split pane is removed. Work follows a focused same-window path:

`Fleet Queue → Driver Workspace → Focused Task Workspace`

Handoff and Unmatched Missing BOL are also focused central routes. WAA does not create driver/BOL/work/Handoff operating-system windows and does not use a browser, WebView, or heavy navigation framework.

## Shell

`MainWindow` remains visible for application lifetime and owns:

- title and current roster/report summary
- Light/Dark action
- `Update Reports`
- `Handoff`
- breadcrumb + Back below queue level
- one `ContentControl` bound to active workspace
- persistent status/progress area

Route state lives in focused view-models plus small `WorkspaceNavigator`; business rules remain in repositories/workflow view-models.

## Routes

- `FleetQueue`
- `DriverWorkspace`
- `IdleTask`
- `MissingBolTask`
- `WorkItemTask`
- `NewWork`
- `ActivityDetail`
- `Handoff`
- `UnmatchedBol`
- `Unavailable`

Driver routes use durable Driver Code. BOL task routes use persisted BOL item ID; manual work/activity uses persisted work-entry ID. Unit Code/Driver Leader are context only.

Fresh launch starts at Fleet Queue. Deep routes are session state only.

## Fleet Queue

Fleet Queue uses full central width and retains deterministic search by Driver Code/Name/Unit/Leader/Order #, threshold, weighted fleet metrics, report-cycle progress, BOL/Open Work counts, idle status, current queue priority, Next Needing Attention, and virtualized/recycling DataGrid rows.

Single mouse click opens Driver Workspace. Keyboard focus + Enter opens the same route. Up/Down remains normal DataGrid navigation. Double-click is unnecessary.

BOL/Open Work cells are compact summaries; reliable full-row opening takes precedence over fragile cell-specific routing.

## Driver Workspace

Full central page for one Driver Code. Header shows Driver Name/Code, Unit, Leader, report cycle, weighted 28-day/coverage, weighted 7-day, current idle-contact state, Open Work count, and unresolved BOL count.

The page is a work index, not a wall of editors.

### Needs Attention

Each actionable object appears once:

1. unfinished/current idle contact
2. each unresolved Missing BOL item
3. each unresolved manual Waiting/Follow-up item

Linked idle work is represented by the Idle item rather than duplicated as manual work. Linked BOL task is represented by its BOL item rather than duplicated as manual work. Existing links remain source of truth.

Rows are keyboard-accessible and open one focused task workspace.

### Quick Actions

- Add Work
- Next Work Item
- Missing BOL focus
- Open Work focus
- Next Needing Attention

### Today’s Activity

Compact local-day activity list. Rows may open read-only ActivityDetail. When no work needs attention, Driver Workspace states:

`No work currently needs attention for this driver.`

## Focused task workspaces

### Idle Task

Shows driver identity/current Unit/Leader, cycle, weighted 28-day/coverage, weighted 7-day, threshold, current outcome, prior note, and optional action note.

Actions: Spoke / Attempted / Spoke — Follow-up. Existing atomic event+linked-work transaction is unchanged. Saving refreshes state without silently routing away. Next Work Item/Next Needing Attention remain explicit.

### Missing BOL Task

One order at a time. Shows Order #, Empty Call Date, route, supported customer/miles, exact source code/name evidence, latest-source presence, warnings, current status, optional note, and action history.

Actions remain Requested / Attempted / Follow-up / Resolved / Reopen as permitted. Exact-code identity, one linked task, action history, transaction boundaries, absence-never-resolves, synchronized Resolve/Reopen, and no-fuzzy rules remain unchanged.

### Manual Work Item

Shows original status/text/time/source, Unit/Leader/report-cycle snapshots, resolution state, and Driver Code. Ordinary Waiting/Follow-up may Resolve/Reopen. MissingBolTask cannot bypass synchronized BOL workflow.

### New Work

One multiline editor with Done / Waiting / Follow-up. Existing whitespace prevention, duplicate-submit protection, retry-text retention, transaction, and context snapshots remain. Success returns to same Driver Workspace and keeps saved item in context.

### Activity Detail

Read-only saved activity/context. No edit/delete path.

## Handoff

Handoff is a central full-width route with:

- Back to Queue
- Regenerate
- editable draft
- Copy to Clipboard

First session visit generates from saved work. Navigating away/back preserves the edited draft. Regenerate intentionally replaces it. Editing/copying never mutates repository state.

### v0.4.2 compact runtime draft

The visible draft is driver-grouped rather than split into the old visible `NEEDS FOLLOW-UP`, `WAITING / PENDING`, and `COMPLETED TODAY` sections.

It begins with the editable convention:

`No open ACE/ACI's`

WAA does not model/validate ACE/ACI state; the user must edit that line when it is not true.

Then WAA emits at most one alphabetical narrative line per driver, preferring current fleet Unit Code/Driver Name. Relevant unresolved work and current-day activity are combined. Idle text keeps concise action + human note and omits generated 28D/7D boilerplate from the copied prose while saved metrics remain intact. WAA does not invent coached/not-coached state or other unstored facts.

The draft ends with:

`Missing BOLs:`

Each driver appears once in that section with all unresolved matched Order # values grouped on the same line. The copied BOL line intentionally omits Empty Call Date, route, and local status because those details remain in the focused BOL workspace.

See `docs/WORK_LOG_HANDOFF.md` for the complete format/data contract.

## Unmatched Missing BOL

Unmatched count opens central read-only route with Order #, Empty Call Date, source code/name, route, latest presence, and exact-match explanation. No manual/fuzzy assignment.

## Back and breadcrumbs

Routes below Fleet Queue expose breadcrumb + Back, e.g.:

- `Fleet`
- `Fleet > Alex Example`
- `Fleet > Alex Example > Idle`
- `Fleet > Alex Example > Missing BOL > BOL-100`

WorkspaceNavigator keeps a real in-session stack. Task Back returns to actual prior Driver Workspace. Handoff/Unmatched deliberately return to Fleet Queue.

Alt+Left invokes Back when available unless focus is inside TextBoxBase/PasswordBox; normal text editing is not hijacked.

## Session-state preservation

Within one run WAA preserves:

- queue search
- threshold text/value
- selected Driver Code
- deterministic queue ordering
- selected row/useful scroll position where virtualization permits
- current driver/focus context
- per-driver New Work drafts
- per-order BOL note drafts
- edited Handoff draft

Returning from task keeps same driver. Returning to Fleet retains search/selection. Deep route is not persisted across restart.

## Report update while navigated

Update Reports remains globally available. Before refresh MainViewModel records route/selected Driver Code; after reload it rebuilds by stable IDs where possible. Unsaved New Work/BOL notes remain in session.

Missing driver/item renders explicit `Unavailable` workspace with safe return path rather than stale-view-model crash.

## Next Work Item

Current driver actionable order:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual Follow-up, oldest first
4. manual Waiting, oldest first
5. other supported unresolved manual work

Next Work Item moves in that list and does not create a second fleet priority engine. When no current-driver item remains it reuses existing search-respecting Next Needing Attention.

## Performance boundaries

Central workspace is presentation/navigation, not a second data layer.

- aggregate/indexed fleet reads
- no one-query-per-driver/row path
- selected-driver work/BOL loads only for Driver Code/state refresh
- one BOL action history loads only when focused BOL task opens
- no timer/watcher/recurring DB polling/query-per-keystroke
- queue virtualization/recycling remains enabled
- database/report operations use bounded off-UI-thread work and short transactions
- hidden legacy split-pane controls/duplicate query paths remain removed

Handoff generation performs one bounded saved-work load plus current fleet context load on first session entry/Regenerate; it does not query per driver.

## Keyboard/accessibility

- fleet row: focus + Enter opens Driver Workspace
- actionable work rows are Button controls with keyboard activation
- visible focus uses FocusBorderBrush
- buttons expose automation names where useful
- Back/breadcrumb/status have clear labels
- status uses words; color is secondary
- restrained cursor/hover/focus/chevron indicates clickable rows
- no mouse-only task action

## Window invariant

`MainWindow` is the only top-level WPF Window. Driver/idle/BOL/work/new-work/activity/Handoff/unmatched/unavailable views are UserControls/DataTemplates hosted in the central ContentControl.

The old split-pane workflow must not be reintroduced in parallel.
