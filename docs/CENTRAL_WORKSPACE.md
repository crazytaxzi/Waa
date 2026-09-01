# WAA Central Workspace v0.4.6

## Purpose

WAA uses one native WPF `MainWindow` as the complete operational shell. Work follows a focused same-window path:

`Fleet Queue → Driver Workspace → Focused Work Task`

Current Missing BOL is report context, not saved work. It appears inside the same central workspace as a dedicated read-only section/detail. Handoff and Unmatched Missing BOL are also central routes. WAA does not create driver/BOL/work/Handoff operating-system windows and does not use a browser, WebView, or heavy navigation framework.

## Shell

`MainWindow` remains visible for application lifetime and owns title/report summary, Light/Dark action, Ambient Motion control, `Update Reports`, `Handoff`, breadcrumb + Back below queue level, one central `ContentControl`, and persistent status/progress area.

Route state lives in focused view-models plus the small `WorkspaceNavigator`; business/source rules remain in repositories/workflow view-models.

## Routes

- `FleetQueue`
- `DriverWorkspace`
- `IdleTask`
- `MissingBolTask` — route name retained internally for compatibility; user-facing page is read-only current-report detail
- `WorkItemTask`
- `NewWork`
- `ActivityDetail`
- `Handoff`
- `UnmatchedBol`
- `Unavailable`

Driver routes use durable Driver Code. Current BOL detail uses a stable in-session Order-derived item ID from the current workbook; it is not a persisted database BOL ID. Manual work/activity uses persisted work-entry ID. Unit Code/Driver Leader are context only.

Fresh launch starts at Fleet Queue. Deep routes are session state only.

## Fleet Queue

Fleet Queue uses full central width and retains deterministic search by Driver Code/Name/Unit/Leader/current BOL Order #, threshold, weighted fleet metrics, report-cycle progress, BOL/Open Work counts, idle status, current queue priority, Next Needing Attention, and virtualized/recycling DataGrid rows.

Single mouse click opens Driver Workspace. Keyboard focus + Enter opens the same route. Up/Down remains normal DataGrid navigation.

Current BOL count is a report count, distinct from Open Work and queue priority.

## Driver Workspace

Full central page for one Driver Code. Header shows Driver Name/Code, Unit, Leader, report cycle, weighted 28-day/coverage, weighted 7-day, current idle-contact state, Open Work count, and current Missing BOL count.

The page is a work index plus bounded current-report context, not a wall of editors.

### Needs Attention

Actionable saved work appears once:

1. unfinished/current idle contact
2. unresolved manual Follow-up work
3. unresolved manual Waiting/other supported manual work

Linked idle work is represented by the Idle item rather than duplicated as manual work.

Current Missing BOL rows are **not** in `NEEDS ATTENTION`.

### Current Missing BOL

A separate `CURRENT MISSING BOL` section lists the selected driver’s current matched workbook rows. The section is read-only and is not stored as work/history. Clicking a row opens one read-only current-report detail.

If no current workbook row exists for the driver, the section says so plainly.

### Quick Actions

- Add Work
- Next Work Item
- Missing BOL focus
- Open Work focus
- Next Needing Attention

### Today’s Activity

Compact local-day saved activity list. Current Missing BOL rows are not activity. Rows may open read-only ActivityDetail.

## Focused work/detail pages

### Idle Task

Shows driver identity/current Unit/Leader, cycle, weighted 28-day/coverage, weighted 7-day, threshold, current outcome, prior note, and optional action note.

Actions: Spoke / Attempted / Spoke — Follow-up. Existing atomic event+linked-work transaction is unchanged.

### Missing BOL detail

One current workbook row at a time. Shows Order #, Empty Call Date, route, supported customer/miles, exact source code/name evidence, current Unit/Leader context, and source-name mismatch warning.

It is explicitly read-only. There is no BOL note editor, Requested / Attempted / Follow-up / Resolved / Reopen control, local BOL status, or BOL action history. Removing the row from the source workbook removes it from WAA after the next accepted report scan.

### Manual Work Item

Shows original status/text/time/source, Unit/Leader/report-cycle snapshots, resolution state, and Driver Code. Ordinary Waiting/Follow-up may Resolve/Reopen.

### New Work

One multiline editor with Done / Waiting / Follow-up. Existing whitespace prevention, duplicate-submit protection, retry-text retention, transaction, and context snapshots remain. Success returns to same Driver Workspace.

### Activity Detail

Read-only saved activity/context. No edit/delete path.

## Handoff

Handoff is a central full-width route with Back to Queue, Regenerate, editable draft, and Copy to Clipboard.

First session visit generates from saved non-BOL work plus the current in-memory Missing BOL workbook view. Navigating away/back preserves the edited draft. Regenerate intentionally rebuilds from current saved work/current BOL rows. Editing/copying never mutates repository/source state.

The draft begins with the editable convention `No open ACE/ACI's`, then compact Driver Leader-grouped saved-work narrative, then `Missing BOLs:` generated from current matched workbook Order # values. Current BOL detail is not persisted merely to produce Handoff.

See `docs/WORK_LOG_HANDOFF.md` for the complete format/data contract.

## Unmatched Missing BOL

Unmatched count opens a central read-only route with current workbook Order #, Empty Call Date, source code/name, route, and exact-match explanation. No manual/fuzzy assignment.

## Back and breadcrumbs

Routes below Fleet Queue expose breadcrumb + Back, e.g.:

- `Fleet`
- `Fleet > Alex Example`
- `Fleet > Alex Example > Idle`
- `Fleet > Alex Example > Missing BOL > BOL-100`

WorkspaceNavigator keeps a real in-session stack. Detail/task Back returns to actual prior Driver Workspace. Handoff/Unmatched deliberately return to Fleet Queue.

Alt+Left invokes Back when available unless focus is inside text-editing controls.

## Session-state preservation

Within one run WAA preserves queue search, threshold, selected Driver Code, deterministic queue ordering, useful selected-row context, current driver/focus context, per-driver New Work drafts, and edited Handoff draft.

There is no per-order BOL note draft because Missing BOL is read-only. Deep route is not persisted across restart; current BOL rows are rebuilt by the next workbook scan.

## Report update while navigated

`Update Reports` remains globally available. After reload WAA rebuilds valid routes using stable Driver Code/item/work IDs where possible. New Work drafts remain in session.

A current BOL detail whose Order is absent from the newly accepted workbook becomes `Unavailable`; WAA does not restore it from SQLite.

## Next Work Item

Current driver actionable order:

1. unfinished idle contact
2. manual Follow-up, oldest first
3. manual Waiting, oldest first
4. other supported unresolved manual work

Current Missing BOL report rows are skipped. When no current-driver work remains, existing search-respecting Next Needing Attention is reused.

## Performance boundaries

Central workspace is presentation/navigation, not a second data layer.

- aggregate/indexed fleet/saved-work reads
- no one-query-per-driver/row path
- current BOL values derive from one bounded in-memory workbook snapshot
- selected-driver saved work/BOL presentation loads on selection/report/route refresh, not each keystroke
- no BOL action-history database load in v0.4.6
- no timer/watcher/recurring DB polling/query-per-keystroke
- queue virtualization/recycling remains enabled
- database/report operations use bounded off-UI-thread work and short transactions
- hidden legacy split-pane controls/duplicate query paths remain removed

Handoff generation performs one bounded saved-work load plus current fleet/current in-memory BOL context on first session entry/Regenerate; it does not query per driver.

## Keyboard/accessibility

- fleet row: focus + Enter opens Driver Workspace
- actionable work rows are Button controls with keyboard activation
- current BOL report rows are keyboard-accessible Buttons opening read-only detail
- visible focus uses FocusBorderBrush
- status uses words; color is secondary
- no mouse-only task action

## Window invariant

`MainWindow` is the only top-level WPF Window. Driver/idle/BOL/work/new-work/activity/Handoff/unmatched/unavailable views are UserControls/DataTemplates hosted in the central ContentControl.

The old split-pane workflow must not be reintroduced in parallel.