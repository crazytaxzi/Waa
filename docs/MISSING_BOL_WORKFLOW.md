# WAA Missing BOL Workflow v0.4.6

This document is authoritative for Missing BOL source reading, exact Driver Code matching, current-file display, unmatched handling, Handoff integration, upgrade compatibility, and scope boundaries.

## Purpose

Missing BOL is a **read-only view of the current `Order Details Missing BOL*.xlsx` workbook**.

WAA does not own a Missing BOL lifecycle. It does not store BOL rows, local BOL status, actions, notes, resolution state, action history, import history, or linked Missing BOL work tasks.

The workbook is the truth:

- if an order is in the current accepted workbook, WAA shows it
- if an order is not in the current accepted workbook, WAA does not show it after the next launch/manual report scan
- restarting WAA does not restore BOL rows from SQLite; the workbook is scanned again

Missing BOL remains a current operational reference integrated into Fleet Queue, Driver Workspace, read-only order detail, Unmatched Missing BOL, search, and the generated Handoff Missing BOL section.

## Runtime source contract

Expected Downloads filename family:

`Order Details Missing BOL*.xlsx`

Temporary Office lock files beginning with `~$` are ignored. Runtime workbooks are read-only and are never modified, renamed, moved, deleted, or saved over.

WAA uses a managed ZIP/XML XLSX reader with no Excel/Office/COM/browser/helper-process dependency.

Supported cells include shared/inline strings, ordinary strings/formula results, numeric cells, zero-padded identifiers according to number format, Excel serial dates, and blanks. Numeric identifiers are rendered as full text without scientific notation and meaningful leading zeros are preserved.

## Worksheet/header selection

WAA examines worksheets in workbook order and selects the first whose first non-empty row contains all required headers. Sheet name is not hard-coded.

Header normalization removes BOM, converts non-breaking spaces, trims outer whitespace, collapses repeated internal whitespace, and compares case-insensitively.

Required headers:

- `Order #`
- `TMEX Order #`
- `Logistics Order#`
- `Bill To`
- `Division#`
- `Empty Call Date`
- `Origin City St`
- `Destination City St`
- `Rev Type`
- `Terminal`
- `Driver Leader`
- `Driver Status`
- `Last Dispatch Driver cd`
- `Last Dispatch Driver nm`
- `Loaded Miles`
- `Order Level Order Miles`

Repeated irrelevant `Terminal Leader` columns are tolerated. A duplicated required header after normalization rejects the workbook as ambiguous. Revenue/financial columns are not required or used.

## Row validation

`Order #` is the current source-row identity used to collapse/validate workbook duplicates.

- trim source text
- normalize uniqueness as uppercase-invariant exact text
- preserve display text
- blank Order # rejects the workbook
- identical duplicate rows collapse
- conflicting duplicate rows reject the workbook

`Empty Call Date` is required and parsed as `DateOnly`; accepted source forms include supported text dates and Excel serial dates.

All rows are parsed and validated in memory before the current in-memory BOL view is replaced.

No BOL row is persisted to SQLite.

## Exact Driver Code matching

The only association rule is:

`TrimUpper(Last Dispatch Driver cd) == TrimUpper(current Driver Code)`

Both sides stay text. Leading zeros are preserved.

Missing BOL current-file matching is against the **current durable fleet roster**. A row whose exact Driver Code is not currently present remains unmatched.

Never match BOL by:

- Driver Name
- Unit Code/truck
- Driver Leader/source leader
- substring/prefix
- punctuation invention
- similarity/fuzzy/probabilistic logic
- manual assignment

`Last Dispatch Driver nm` is evidence only. Exact-code name mismatch keeps the code association, preserves both names, shows a restrained warning, and never overwrites durable Driver Name.

## Current matched rows

Matched workbook rows contribute:

- a compact Missing BOL count on the driver/fleet presentation
- Order # search text
- a dedicated **CURRENT MISSING BOL** read-only section in Driver Workspace
- a read-only same-window order detail when opened
- the current-file Missing BOL section of generated Handoff

They do **not**:

- increase Open Work
- make a driver need attention by themselves
- enter `Next Work Item`
- create work entries
- create Today’s Activity
- create local Requested/Attempted/Follow-up/Resolved/Reopen state
- survive when the current workbook no longer contains them

The read-only order detail may show Order #, Empty Call Date, route, customer, miles, exact source Driver Code/name, current Unit, current Driver Leader, and source-name mismatch warning.

## Unmatched rows

Blank/unknown normalized source Driver Code remains unmatched.

The same-window read-only Unmatched Missing BOL route shows current workbook Order #, Empty Call Date, source code/name, route, and exact-match explanation.

Unmatched rows:

- create no driver-owned work
- do not affect driver priority
- do not enter Handoff
- cannot be manually/fuzzily assigned

A later report scan may match the row if the current durable roster then contains that exact Driver Code.

## Source lifecycle and memory ownership

Missing BOL has no current database tables or current BOL database lifecycle.

At launch and explicit `Update Reports`:

1. resolve the Downloads folder
2. enumerate matching non-lock XLSX candidates newest first
3. stable-read candidate bytes
4. compute SHA-256 for same-session change detection
5. parse/validate fully in memory
6. replace the current in-memory BOL view with the accepted workbook rows
7. derive matched/unmatched presentation from the current durable roster

Same-session identical bytes may be recognized as already current. The hash is not persisted.

If no matching workbook exists, the current BOL view is cleared. If no candidate can be validated, WAA reports the failure and does not invent or restore BOL rows from SQLite.

Rolling 7 Day persistence remains independent.

## Legacy v0.3-v0.4.5 compatibility

Older WAA releases may already have created:

- `missing_bol_imports`
- `missing_bol_items`
- `missing_bol_action_events`
- `missing_bol_work_links`
- generated `MissingBolTask` / `MissingBolAction` work rows

v0.4.6 does **not** destructively drop or rewrite those legacy tables/history during upgrade.

They are dormant compatibility data:

- they are not used to repopulate current Missing BOL rows
- old generated BOL task/action work is classified and excluded from current Open Work and Today’s Activity
- old generated BOL task/action work is excluded from current Handoff
- legacy unresolved BOL task counts are subtracted from current Open Work presentation

This preserves the user’s existing database without letting obsolete BOL workflow state continue driving the current application.

Fresh v0.4.6 databases do not create Missing BOL tables.

## Driver Workspace

Driver Workspace separates actual work from report information:

- `NEEDS ATTENTION` contains idle/manual work that can actually be worked
- `CURRENT MISSING BOL` contains the selected driver’s current workbook rows

BOL rows remain clickable for read-only detail but are not work items.

The old BOL note editor, Requested, Attempted, Follow-up, Resolved, Reopen controls, and BOL action history are removed.

## Next Work Item and queue behavior

`Next Work Item` walks actual actionable driver work only. Current Missing BOL report rows are skipped.

Fleet priority remains based on unfinished idle accountability and real unresolved saved work. Current Missing BOL presence alone does not elevate driver priority.

Search may still find a driver by a current attached BOL Order #.

## Handoff

Handoff continues to have a compact dedicated `Missing BOLs:` section because the current report is useful at shift change.

The BOL section is generated **transiently from the current workbook at Regenerate time**, not from saved BOL work history.

- represented matched drivers use current durable Driver Code ownership
- Driver Leader grouping follows the established Handoff rules
- each represented driver appears once in the BOL section
- current workbook Order # values are grouped on that driver’s line
- current fleet Unit/Driver Name are preferred for identity
- BOL rows do not create the ordinary driver work narrative

Example:

`242163 — Brad Example [ABC123]: Missing BOL for orders AST2543, ASU1575`

Editing/copying Handoff never changes report data. Regenerate rebuilds the Missing BOL section from the current in-memory workbook view plus saved non-BOL work.

## Report-update cadence

Rolling 7 Day and Missing BOL are scanned only:

1. once at launch
2. when the user explicitly chooses `Update Reports`

No `FileSystemWatcher`, recurring timer, polling loop, or automatic mid-session import exists.

## Privacy/permanent exclusions

Public repository fixtures are synthetic only. Never commit production BOL/employee/customer data, production databases, reports, screenshots, or logs.

Permanently excluded unless explicitly reversed:

- emailing/transmitting documents
- automatic calls/messages/contact
- OCR/image recognition
- document upload/storage/attachments
- separate BOL dashboards/analytics/revenue reporting
- fuzzy/name/unit/truck/leader/probabilistic matching
- complex escalation/routing/approval logic
- browser/WebView/local server/Node/cloud/helper processes

## Honest limitations

- current unmatched rows remain unmatched until an exact current durable Driver Code exists
- no manual reassignment by design
- BOL work/action history from older releases is not exposed as current BOL workflow state
- WAA does not store a coaching/not-coached field and must not invent one
- Maintenance and DOT remain separate future evaluations