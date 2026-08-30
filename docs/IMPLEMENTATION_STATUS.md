# WAA Implementation Status

## Current bounded milestone

**WAA Central Workspace + Theme-Safe Text v0.4 — implementation complete on the feature branch; final Windows/main validation pending.**

This document must be updated to the exact successful run/test count before v0.4 is considered released. The validated v0.3 baseline remains the fallback authority until the complete v0.4 workflow passes and the change is merged.

## Runtime and deployment

- .NET 8 native WPF desktop application
- one top-level `MainWindow`
- warnings treated as errors
- Windows x64 self-contained portable publish
- no installer, administrator requirement, SDK, separately installed .NET runtime, Excel, or Office required by the published build
- one local desktop process
- SQLite database/preferences under `%LOCALAPPDATA%\WAA`
- GitHub Actions performs restore, Release build, WPF/XAML compilation, tests, portable publish, and artifact upload

## v0.4 central workspace implementation

### Shell and routes

- one persistent `MainWindow` shell
- one central `ContentControl` bound to `CurrentWorkspace`
- no separate driver/task/BOL/Handoff top-level Windows
- old always-visible fleet + selected-driver split pane removed
- current focused routes:
  - FleetQueue
  - DriverWorkspace
  - IdleTask
  - MissingBolTask
  - WorkItemTask
  - NewWork
  - ActivityDetail
  - Handoff
  - UnmatchedBol
  - Unavailable stale-entity state
- durable Driver Code owns driver routes; persisted item/work-entry IDs own task routes
- fresh launch defaults to Fleet Queue; deep route is not restart-persisted

### Fleet Queue

- full central width
- search/threshold/fleet metrics/update summary preserved
- virtualized/recycling DataGrid preserved
- idle values/status, BOL count, Open Work count, queue ordering, and Next Needing Attention preserved
- single row click opens Driver Workspace
- keyboard focus + Enter opens Driver Workspace
- normal DataGrid Up/Down navigation retained
- queue search and selected Driver Code survive round-trip navigation

### Driver Workspace

- full-page Driver Name/Code/Unit/Leader/report-cycle summary
- weighted 28-day/7-day idle and current idle-contact state
- Open Work and Missing BOL counts
- compact `NEEDS ATTENTION` index
- one idle item rather than duplicate linked idle work
- one BOL item per unresolved order rather than duplicate linked MissingBolTask row
- manual Waiting/Follow-up items appear once
- Quick Actions: Add Work, Next Work Item, Missing BOL focus, Open Work focus, Next Needing Attention
- compact Today’s Activity list with read-only Activity Detail route
- professional no-open-work empty state

### Focused task workspaces

Idle Task:

- Driver/Unit/Leader/report-cycle context
- weighted 28-day value + coverage, 7-day value, threshold, current outcome, prior note
- optional note
- Spoke / Attempted / Spoke — Follow-up
- existing atomic idle-event/linked-work transaction preserved

Missing BOL Task:

- one order at a time
- Order #, Empty Call Date, route, supported customer/miles context
- exact source Driver Code/name evidence
- latest-source presence and name/presence warnings
- current local status
- optional note draft
- Requested / Attempted / Follow-up / Resolved / Reopen
- compact action history loaded for the opened task
- existing exact-code identity, one linked task, atomic action/activity save, absence-never-resolves, and synchronized Reopen behavior preserved

Manual Work Item:

- original status/text/created time/source
- Unit/Leader/report-cycle snapshots
- resolution state
- Resolve/Reopen for supported ordinary work
- MissingBolTask still cannot bypass synchronized BOL workflow

New Work:

- one focused multiline editor
- Done / Waiting / Follow-up
- existing whitespace/double-submit/transaction/retry-text protections
- successful save returns to same Driver Workspace and keeps the saved entry in context

Activity Detail:

- read-only saved activity/context
- no edit/delete mutation path

Handoff:

- central full-width route
- Regenerate / editable draft / Copy to Clipboard
- deterministic saved-work sections preserved
- edited draft survives navigation away/back during the session
- Regenerate intentionally replaces the current edit

Unmatched BOL:

- central full-width read-only route
- Order/date/source code/source name/route/latest-presence/exact-match explanation
- no manual/fuzzy assignment

### Navigation and state preservation

- `WorkspaceNavigator` provides explicit location/back-stack state
- Back/breadcrumbs reflect current route
- task Back returns to the actual prior Driver Workspace
- Alt+Left performs safe Back only outside TextBox/PasswordBox editing
- search, selected Driver Code, current driver, per-driver New Work drafts, per-order BOL note drafts, and Handoff draft survive in-session navigation
- report update captures/restores current route by stable IDs
- unsaved New Work/BOL notes survive report refresh
- stale/missing entities render `Unavailable` with a safe Back path instead of crashing
- route is not persisted across application restart

### Next Work Item

Within one driver:

1. unfinished idle contact
2. unresolved Missing BOL, oldest Empty Call Date first
3. manual Follow-up, oldest first
4. manual Waiting, oldest first
5. other supported unresolved manual work

When no next item exists, WAA reuses existing visible/search-respecting `Next Needing Attention`; no second fleet-priority engine is introduced.

## v0.4 theme implementation

### Resource ownership

- `Themes/LightColors.xaml` owns Light literal colors
- `Themes/DarkColors.xaml` owns Dark literal colors
- both palettes contain matching required key sets
- `Themes/BaseStyles.xaml` owns theme-aware control styles
- `App.xaml` merges active palette + base styles and central workspace DataTemplates
- `ThemeManager` swaps only the active palette dictionary
- no view-model exposes WPF Brush objects

### Automatic ordinary text

Implicit/base styles use `DynamicResource` for current controls including Window, TextBlock, Label, ContentControl, Button, TextBox, RichTextBox, ToolTip, DataGrid/row/cell/header, DataGrid generated display/edit elements, ListBox/ListView items, ComboBox items, CheckBox, RadioButton, GroupBox, TabItem, MenuItem, and Hyperlink.

Dedicated theme roles include:

- Text / Subtle / Disabled
- Primary button text
- Selected row text
- Link text
- Warning / Follow-up / Completed / Quiet / Error / Information text + paired semantic backgrounds
- control/panel/grid/focus borders and surfaces

DataGridTextColumn display/edit elements explicitly follow the current DataGridCell/theme foreground rather than relying on Windows defaults.

### Live switching and persistence

- Light/Dark switch updates visible controls immediately through dynamic resources
- current route/search/selection/drafts are not reset
- title-bar mode updates where Windows DWM support permits
- appearance preference remains SQLite-persisted
- preference write runs off the UI thread
- save failure restores the prior visible theme and reports the error
- primary-button hover keeps the primary foreground/background pair rather than receiving the generic button hover surface

### Theme audit and contrast

Repository tests inspect all current `src/Waa.App` XAML/C# source and fail for inappropriate fixed theme colors outside the palette dictionaries, including hard-coded foreground/background hex values, named fixed foregrounds, `Brushes.*`, arbitrary `SolidColorBrush`, fixed Color construction, and theme brushes incorrectly used as StaticResource.

Contrast tests read actual palette values and enforce:

- at least 4.5:1 for normal/important text combinations
- at least 3:1 for relevant boundaries/focus indicators
- both ordinary and primary-button hover text/background combinations
- semantic warning/follow-up/completed/information/quiet pairs
- selected rows, DataGrid headers, editors, disabled controls, links, and normal surfaces

## Preserved v0.3 business/data behavior

### Reports and roster

- Rolling 7 Day and Missing BOL update once at launch, then only via `Update Reports`
- no watcher/timer/polling path
- stable read/SHA-256/idempotent atomic import
- source outcomes remain independent with last-known-good preservation
- Driver Code durable identity; Unit/Leader remain context
- ten-character Driver Leader support remains

### Idle

- weighted 7-day and complete-coverage weighted 28-day calculations
- configurable strict-greater-than threshold
- current-cycle contact state and same-cycle/new-cycle semantics
- immutable metric/threshold/unit/leader/source snapshots
- existing four-band queue priority

### Missing BOL

- managed local read-only XLSX parsing
- Order # durable item identity
- exact normalized source Driver Code matching only
- no fuzzy/name/unit/truck/leader matching
- unmatched preservation/later exact attachment
- one linked task per item
- append-only actions
- disappearance never resolves
- reappearance does not automatically reopen
- source reassignment conflicts reject the snapshot
- atomic item/task/action/activity writes

### Work log and Handoff

- Done / Waiting / Follow-up
- unresolved carry-forward
- Resolve/Reopen preserves history
- one linked work entry per idle event
- MissingBolTask synchronization guard
- Today’s Activity/local-day semantics
- deterministic Handoff generation and Copy to Clipboard

## Database compatibility result

v0.4 requires **no database schema change** and does not increment the schema version.

Existing data remains under `%LOCALAPPDATA%\WAA`, including roster/imports/observations, idle contacts, work entries, Missing BOL imports/items/actions/work links, threshold, theme preference, and Handoff source data. Replacing the portable application folder leaves that data folder untouched.

## Validation status

Validated v0.3 baseline:

- 24 core tests
- 65 app/integration tests
- 89 total
- zero failures/skips/build warnings
- WPF/XAML build and self-contained Windows x64 publish successful

Current v0.4 branch already produced a successful intermediate Windows run with 24 core + 153 app tests (177 total), zero failures/skips, zero build warnings, and successful portable publish. Subsequent audit fixes added interaction/hover regression coverage and documentation, so that run is **superseded** and is not the release artifact.

The exact final v0.4 test count, workflow run, commit SHA, artifact, and SHA-256 will be recorded here after the complete latest branch workflow passes and the final merged `main` workflow succeeds.

## Remaining limitations

Not implemented:

- manual/fuzzy assignment of unmatched BOL items
- emailing/transmitting BOLs/documents
- automatic calls/messages/contact
- OCR/image recognition/document uploads/storage/attachments
- BOL analytics/revenue dashboards
- escalation/routing/approval engines
- maintenance workflow
- DOT workflow
- destructive work-entry deletion
- full corrective idle-event editing/audit UI
- dedicated Driver Leader filter control
- measured benchmark on the user’s representative low-end office PC

These are not hidden placeholders. Permanent exclusions remain excluded; maintenance and DOT remain separate future evaluations.
