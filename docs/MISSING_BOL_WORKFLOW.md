# WAA Missing BOL Workflow v0.4.2 Presentation / v0.3 Data Contract

This document is authoritative for Missing BOL ingestion, exact-code matching, source lifecycle, local states/actions, linked work, queue behavior, Handoff integration, unmatched handling, and permanent scope boundaries. v0.4.x changes presentation/navigation only; the validated v0.3 BOL data/business contract remains authoritative.

## Purpose and scope

Missing BOL is a small local work queue integrated into Fleet Queue, Driver Workspace, one-order-at-a-time Missing BOL Task workspaces, unified work history, and Handoff.

It is not a document repository, communication system, OCR tool, analytics dashboard, revenue report, fuzzy matcher, or escalation platform. WAA never sends requests, calls drivers, stores attachments, reads images, or guesses identity.

## Runtime source contract

Expected Downloads filename family:

`Order Details Missing BOL*.xlsx`

Temporary Office lock files beginning with `~$` are ignored. Runtime workbooks are read-only evidence and are never modified, renamed, moved, deleted, or saved over.

WAA uses a bounded managed ZIP/XML XLSX reader with no Excel/Office/COM/browser/helper-process dependency.

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

## Row validation and identity

`Order #` is the durable Missing BOL source-item key.

- trim source text
- normalize uniqueness as uppercase-invariant exact text
- preserve display text
- blank Order # rejects complete workbook
- identical duplicate rows collapse
- conflicting duplicate rows reject complete workbook

`Empty Call Date` is required and stored as `DateOnly`; accepted source forms include text date formats and Excel serial dates. Invalid required values reject the complete workbook before mutation.

Origin/destination/customer/miles/source-name/source-code context may be blank. Blank/unknown source Driver Code makes the item unmatched; it does not invalidate the order.

All rows are parsed/validated in temporary memory before a database transaction begins.

## Exact Driver Code matching

The only association rule is:

`TrimUpper(Last Dispatch Driver cd) == TrimUpper(Driver Code)`

Both sides stay text. Leading zeros are preserved. Matching uses all durable known drivers, not only current roster rows.

Never match BOL by:

- Driver Name
- Unit Code/truck
- Driver Leader/source leader
- substring/prefix
- punctuation invention
- similarity/fuzzy/probabilistic logic
- manual automatic association

`Last Dispatch Driver nm` is evidence only. Exact-code name mismatch keeps the code association, preserves both names, shows a restrained warning, and never overwrites durable Driver Name.

## Unmatched items

Blank/unknown normalized source Driver Code remains unmatched.

The same-window read-only Unmatched Missing BOL route shows Order #, Empty Call Date, source code/name, route, latest-source presence, and exact-match explanation.

Unmatched items:

- create no driver-owned task
- do not affect driver priority
- do not enter Handoff
- cannot be manually/fuzzily assigned

If a later Rolling 7 Day import introduces the exact Driver Code, the existing item attaches and its linked task is created exactly once. Repeated checks are idempotent.

## Database/source lifecycle

Established non-destructive tables:

- `missing_bol_imports`
- `missing_bol_items`
- `missing_bol_action_events`
- `missing_bol_work_links`

Indexes support exact source code, matched-driver unresolved reads, status/date/presence, action history, aggregate counts, and task/action links.

v0.4.x adds no BOL schema migration and does not increment schema version.

Accepted workbook lifecycle:

1. resolve actual Downloads known folder
2. enumerate matching non-lock XLSX candidates newest first
3. stable-read candidate
4. SHA-256 complete bytes
5. skip accepted hash
6. parse/validate fully in memory
7. begin transaction
8. insert import metadata
9. mark previous items absent from newest accepted source
10. upsert by exact normalized Order #
11. update source/last-seen context
12. preserve local status/resolution/task/action history
13. attach exact Driver Codes
14. create task only for matched unresolved item without one
15. commit entire snapshot

Database failure rolls back entire snapshot.

## Source disappearance, return, conflict

Disappearance from a later accepted workbook never resolves or deletes an item/task.

Unresolved absent item keeps local state/task and shows `Not in latest report`.

Resolved item that reappears stays resolved, is flagged present again, and may be explicitly reopened; it is never reopened automatically.

If an existing Order # later arrives under a different normalized source Driver Code, reject the new snapshot rather than moving driver-owned history.

## Linked task

A newly matched unresolved item owns exactly one linked open FollowUp `MissingBolTask` work entry.

Persisted task text includes deterministic source context, e.g.:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots Driver Code, Unit, Leader, report cycle when available, source import, and creation UTC. Reimport updates source/status wording on the same task. Resolve resolves the same task. Reopen reopens the same task. No operation creates a duplicate.

Driver Workspace renders one Missing BOL attention row per unresolved order and does not duplicate the linked task as manual work. Generic Work Item Resolve/Reopen cannot bypass BOL synchronization; UI and database guard require Missing BOL actions.

## Local statuses/actions

Statuses:

- Open
- Requested
- Attempted
- FollowUp (display Follow-up)
- Resolved

Reopen is an action returning Resolved → Open.

Each Requested/Attempted/Follow-up/Resolved/Reopen:

- changes current item/task state as appropriate
- appends one action event
- creates one completed `MissingBolAction` activity entry
- retains optional trimmed note
- saves item/task/action/activity atomically

Failure rolls the entire action back. Duplicate submit is disabled while saving and failed-save note text is retained. Unsaved BOL note drafts also survive in-session navigation/report refresh.

## Queue/search integration

Fleet rows expose aggregate Open Work and unresolved matched BOL counts. Counts/oldest dates/Order # search text are indexed aggregate reads, not one query per row.

Priority remains:

1. unfinished high-idle contact
2. above-threshold Spoke, unresolved work before clear
3. remaining unresolved work including MissingBolTask
4. clear drivers

Missing BOL never buries unfinished high-idle accountability. Within otherwise equal ordinary unresolved work, older Empty Call Date may break ties. Search includes attached Order # by deterministic substring and `Next Needing Attention` respects visible results.

## Driver Workspace / Missing BOL Task

Driver Workspace shows compact unresolved BOL attention rows under `NEEDS ATTENTION`; each order appears once.

Opening a row navigates inside MainWindow to one focused task with:

- driver identity/current Unit/Leader
- Order # and Empty Call Date
- route/customer/miles where available
- exact source Driver Code/name evidence
- latest-report presence and warnings
- current local state
- optional action note
- Requested/Attempted/Follow-up/Resolved/Reopen as permitted
- compact action history
- Next Work Item / Next Needing Attention

Back returns to prior Driver Workspace. No secondary Window/modal/browser is created.

## Next Work Item

Within one driver, unresolved BOL follows unfinished idle contact and precedes manual Follow-up/Waiting. BOL ordering is oldest Empty Call Date first. When current driver has no next work, WAA reuses existing visible/search-filtered `Next Needing Attention`.

## Work history and v0.4.2 Handoff

Persisted `MissingBolTask` remains the unresolved work source. `MissingBolAction` remains completed activity and Today’s Activity source. Resolved task itself is not duplicated as completion because the linked Resolved action represents that activity.

The **runtime v0.4.2 Handoff presentation is compact**:

- unresolved MissingBolTask does not appear as one verbose handoff line per order
- the generated draft has a dedicated `Missing BOLs:` section
- each driver appears once in that section
- all unresolved matched Order # values are grouped on the driver’s line
- singular/plural wording is automatic (`order` / `orders`)
- BOL orders are deterministically oldest Empty Call Date first, then Order #
- the visible copied BOL line intentionally omits Empty Call Date, route, and local status because those details remain in the focused BOL workspace

Example:

`242163 — Brad Example [ABC123]: Missing BOL for orders AST2543, ASU1575`

Current fleet Unit/Driver Name are preferred for Handoff identity when available; saved task snapshot is fallback.

MissingBolAction activity may contribute to the driver’s compact narrative line. When the action has a human note, Handoff prefers that note instead of repeating mechanical `Resolved missing BOL... Note:` boilerplate. When no note exists, concise saved action text remains so activity is not silently lost.

The old visible `NEEDS FOLLOW-UP` / `COMPLETED TODAY` BOL presentation is not the v0.4.2 runtime format. Underlying local-day/open-state classification remains deterministic and regression-tested.

Editing/copying Handoff never changes BOL/task/action/source state. Regenerate rebuilds from saved records; navigating away/back preserves edited draft in-session.

## Report-update independence

Rolling 7 Day and Missing BOL scan only once at launch and through explicit `Update Reports`. Sources import independently and status reports partial outcomes honestly.

No FileSystemWatcher/timer/polling/recurring scan exists.

If Update Reports runs while a BOL task is open, the route is rebuilt by stable Driver Code/item ID; unsaved note text is preserved. Missing/stale entity shows explicit Unavailable state with safe Back.

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

- unmatched items cannot become driver-owned work/Handoff until exact durable Driver Code exists
- no manual reassignment by design
- v0.4.2 opening `No open ACE/ACI's` is an editable handoff convention; WAA does not track or validate ACE/ACI state
- WAA does not store a coaching/not-coached field, so Handoff must not invent one
- representative low-end office-PC benchmark remains separate from GitHub-hosted validation
- Maintenance and DOT remain separate future evaluations
