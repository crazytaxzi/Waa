# WAA Missing BOL Workflow v0.4 Presentation / v0.3 Data Contract

This document remains the authoritative product and technical specification for Missing BOL ingestion, exact-code matching, source lifecycle, local states/actions, linked work, queue behavior, Handoff integration, unmatched handling, and permanent scope boundaries. v0.4 changes presentation/navigation only; the validated v0.3 BOL data/business contract is preserved.

## Purpose and scope

Missing BOL is a small local work queue integrated into Fleet Queue, Driver Workspace, one-order-at-a-time Missing BOL Task workspaces, unified work history, and deterministic Handoff.

It is not a document repository, communication system, OCR tool, analytics dashboard, revenue report, matching engine, or escalation platform. WAA never sends a request, calls a driver, stores an attachment, reads an image, or guesses an identity.

## Runtime source contract

Expected Windows Downloads filename family:

`Order Details Missing BOL*.xlsx`

Accepted examples include:

- `Order Details Missing BOL.xlsx`
- `Order Details Missing BOL (1).xlsx`
- `Order Details Missing BOL-new.xlsx`

Temporary Office lock files beginning with `~$` are ignored.

The runtime workbook is read-only source evidence. WAA never modifies, renames, moves, deletes, rewrites, or saves over it.

## XLSX reader boundary

WAA uses a bounded managed ZIP/XML reader. It requires no Excel installation, Office, COM automation, Internet access, administrator access, browser runtime, or helper process.

Supported cell representations:

- shared strings
- inline strings
- ordinary string/formula-result cells
- numeric cells
- zero-padded numeric identifiers according to workbook number format
- Excel serial dates
- blank cells

Numeric identifiers are rendered as full invariant text without scientific notation. Leading zeros are preserved when workbook format supplies them. Driver codes and order identifiers are never durable integers.

## Worksheet and header selection

WAA examines worksheets in workbook order and selects the first worksheet whose first non-empty row contains every required header. Sheet name is not hard-coded.

Header normalization:

1. remove a BOM if present
2. convert non-breaking spaces to ordinary spaces
3. trim outer whitespace
4. collapse repeated internal whitespace
5. compare case-insensitively

Required persisted headers:

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

Repeated irrelevant `Terminal Leader` columns are tolerated. A required header repeated after normalization is ambiguous and rejects the complete workbook. `Driver Leader` remains distinct from `Terminal Leader`.

WAA does not require/use Total Revenue, billing/AR staff, Buyer, Carrier, Dray Name, or duplicate Terminal Leader values.

## Row validation and source-item identity

`Order #` is the durable Missing BOL source-item key.

- trim source text
- normalize uniqueness as uppercase-invariant exact text
- preserve source display text
- blank Order # rejects the complete workbook
- truly identical repeated rows collapse to one item
- conflicting rows for one normalized Order # reject the complete workbook and identify the source rows

`Empty Call Date` is required and stored as `DateOnly`. Accepted forms include Excel numeric/serial dates, `M/d/yy`, `M/d/yyyy`, zero-padded equivalents, and ISO `yyyy-MM-dd`. Invalid values reject the workbook and identify the Order #, worksheet, and cell.

Origin, destination, Bill To, miles, source name/code, and other context may be blank. Blank/unknown source Driver Code makes the item unmatched; it does not invalidate the order.

All rows are parsed and validated into temporary memory before database mutation. Structurally invalid workbooks never partially import.

## Exact Driver Code matching

The only matching rule is:

`TrimUpper(Last Dispatch Driver cd) == TrimUpper(Driver Code)`

Both sides remain text and meaningful leading zeros are preserved. Matching uses all durable driver entities already known to WAA, not only current roster rows.

WAA never matches Missing BOL by:

- Driver Name
- Unit Code
- truck assignment
- Driver Leader
- source leader
- substring/prefix
- punctuation cleanup/invention
- name similarity
- fuzzy/probabilistic logic
- manual automatic association

`Last Dispatch Driver nm` is supporting evidence only. If exact Driver Code matches but source name differs, WAA preserves both names, displays a restrained data-quality warning, and does not overwrite durable Driver Name or split identity.

## Unmatched items

A blank or unknown normalized source Driver Code remains unmatched.

v0.4 exposes unmatched items in a focused **Unmatched Missing BOL** route in the same MainWindow. It is read-only and shows:

- Order #
- Empty Call Date
- source Driver Code
- source Driver Name
- route
- latest-source presence
- an explicit explanation that no exact durable Driver Code currently exists

Unmatched items create no driver-owned task, do not affect driver priority, and do not enter Handoff. There is no manual/fuzzy assignment action.

If a later Rolling 7 Day import introduces the exact Driver Code, the existing item attaches automatically by exact code and its linked task is created once. Repeating attachment checks is idempotent.

## Database model and migration

Missing BOL uses the established non-destructive tables:

- `missing_bol_imports` — accepted workbook metadata/hash/time/row count
- `missing_bol_items` — source evidence, exact association, local status, presence, resolution, task link
- `missing_bol_action_events` — append-only local action history and linked activity entry
- `missing_bol_work_links` — task/action work provenance, item ID, source import ID

Indexes support normalized source Driver Code, matched-driver unresolved reads, status, Empty Call Date, latest-source presence, action history, aggregate counts, and task/action links.

v0.4 navigation/theming adds **no database migration** and does not increment schema version. Existing v0.3 BOL tables/state are reused unchanged.

## Accepted workbook lifecycle

Each accepted workbook is one source snapshot:

1. resolve actual Windows Downloads known folder
2. enumerate matching non-lock `.xlsx` files newest-last-write first
3. stable-read a candidate
4. hash complete bytes with SHA-256
5. skip an already accepted hash
6. parse/validate completely in memory
7. begin one SQLite transaction
8. insert import record
9. mark prior items not present in newest source
10. upsert every row by normalized exact Order #
11. update source context/last-seen values
12. preserve local status, resolution, task, notes, action history
13. match exact durable Driver Codes
14. create a task only for matched unresolved item without one
15. commit complete snapshot

A database error rolls back the complete snapshot.

## Source disappearance and return

Disappearance from a later accepted workbook never resolves/deletes an item.

For an unresolved absent item:

- keep local status
- keep same open task
- set `is_present_in_latest_import = false`
- display `Not in latest report`

For a resolved item that appears again:

- keep Resolved/timestamp
- keep same resolved task
- mark present again
- display `Resolved — present again in latest report`
- offer Reopen
- never reopen/contact automatically

If a later workbook puts an existing normalized Order # under a different normalized source Driver Code, reject the new snapshot rather than moving driver-owned work/history. Prior accepted item/task/action state stays intact.

## Linked task behavior

A newly matched unresolved item owns exactly one linked open FollowUp work entry (`MissingBolTask`).

Task text uses available source context, for example:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots matched Driver Code, Unit Code, Driver Leader, report cycle when available, source import, and creation UTC.

Reimport updates source context/status wording in the same task. It does not create another task. Resolve sets the linked task resolution timestamp. Reopen clears that timestamp on the same task.

In v0.4 Driver Workspace, an unresolved BOL appears as **one Missing BOL attention row**, not as both the BOL item and its linked task. The linked task remains the persisted work source for work history/Handoff. Generic Work Item controls still cannot Resolve/Reopen a MissingBolTask; UI instruction and the database guard require synchronized BOL controls.

## Local statuses and actions

Current statuses:

- Open
- Requested
- Attempted
- FollowUp (displayed Follow-up)
- Resolved

Reopen is an action that returns Resolved to Open.

### Requested

- set status Requested
- keep item/task unresolved
- append action event
- create completed MissingBolAction activity: `Requested missing BOL for order …`

### Attempted

- set status Attempted
- keep item/task unresolved
- append action event
- create completed activity: `Attempted contact regarding missing BOL for order …; driver not reached.`

### Follow-up

- set status FollowUp
- keep item/task unresolved
- append action event
- create completed activity: `Missing BOL for order … requires follow-up.`

### Resolved

- set status Resolved/resolved UTC
- resolve linked task at same UTC
- append action event
- create completed activity: `Resolved missing BOL for order …`

### Reopen

- set status Open and clear resolved UTC
- reopen same linked task
- append action event
- create completed activity: `Reopened missing BOL for order …`

Optional note is trimmed and appended once to activity text while remaining in action history. Prior events are never overwritten.

## Action atomicity and retry behavior

Requested/Attempted/Follow-up commit item status, task text/state, action event, and completed activity together.

Resolved commits item resolution, task resolution, action event, and completed activity together.

Reopen commits item reopen, same-task reopen, action event, and completed activity together.

Any failure rolls back the complete action. Item commands disable duplicate submission while saving. Failed save retains typed note text for retry.

v0.4 also retains per-item unsaved notes during in-session navigation/report refresh so route rebuilding does not silently discard text.

## Queue and search integration

Fleet Queue rows show:

- Open Work: all unresolved work
- BOL: unresolved matched Missing BOL subset

Counts, oldest dates, and Order # search text are aggregate/indexed reads, not one query per driver/order.

Priority remains:

1. unfinished high-idle contact: Spoke — Follow-up, Attempted, Not Contacted
2. above-threshold Spoke, unresolved work before clear
3. remaining unresolved work including MissingBolTask
4. clear drivers

Missing BOL cannot bury unfinished high-idle work. Within otherwise equal ordinary unresolved drivers, older Empty Call Date may sort first; Driver Name/Code remain stable tie-breakers.

Search includes attached Order # text through deterministic substring matching. `Next Needing Attention` respects active visible search results and naturally selects a driver whose only issue is Missing BOL.

## v0.4 Driver Workspace and Missing BOL Task presentation

The old selected-driver pane with all BOL forms simultaneously is removed.

Driver Workspace shows Missing BOL only as compact actionable rows under `NEEDS ATTENTION`, plus a count/section focus action. Each unresolved order appears once.

Opening a BOL row navigates to one focused Missing BOL Task page inside MainWindow. It displays:

- Driver Name/Code and current Unit/Driver Leader context
- Order #
- Empty Call Date
- route
- supported customer and mileage context
- exact source Driver Code
- source Driver Name evidence
- latest-report presence
- name/presence warnings
- current local status
- optional action note
- Requested / Attempted / Follow-up / Resolved / Reopen as state permits
- compact persisted action history
- Next Work Item / Next Needing Attention

Task Back returns to the actual prior Driver Workspace. No secondary Window/modal/browser is created.

All BOL text/background/status colors use theme-aware dynamic resources. Semantic color supplements explicit status text; it is never the only state signal.

## Next Work Item

Within one driver, unresolved Missing BOL items come after unfinished idle contact and before manual Follow-up/Waiting. BOL items use repository order with oldest Empty Call Date first.

After the current driver has no next work, WAA reuses existing visible/search-filtered `Next Needing Attention` rather than inventing another queue engine.

## Work history and Handoff

`MissingBolTask` appears in persisted Open Work/Handoff Needs Follow-up while unresolved, but Driver Workspace does not duplicate it as a second actionable manual row.

`MissingBolAction` appears in Today’s Activity and `COMPLETED TODAY` when created during the current local day.

A resolved task is excluded from Completed Today so the Resolved action is the one completion line. Reopen returns the same task to Needs Follow-up and adds one Reopened action line to Completed Today.

Handoff is a focused route in the same MainWindow. Lines use saved task/action text and snapshot Unit Code context. Editing/copying the draft never changes BOL items, tasks, events, source state, reports, or identity. Regenerate intentionally rebuilds from current saved work; navigating away/back preserves the edited draft in-session.

## Report-update independence and navigated refresh

Rolling 7 Day and Missing BOL are scanned only:

1. once at launch
2. through explicit `Update Reports`

Each source imports independently. Combined status reports honest partial outcomes. Missing workbook does not mark known items absent; accepted hash reports already current; invalid/locked/conflicting source preserves last accepted BOL state.

There is no FileSystemWatcher, timer, polling, recurring scan, or automatic mid-session refresh.

If Update Reports runs while a Missing BOL Task is open, v0.4 rebuilds the route by stable Driver Code/item ID. Unsaved local note text is preserved. If the task no longer exists, WAA shows an explicit Unavailable state with a safe Back path instead of crashing on stale references.

## Privacy and permanent exclusions

The public repository contains only synthetic BOL workbooks/items, driver identities, routes, customers, orders, databases, and logs. Production reports/data are runtime read-only inputs and are never committed.

Permanently excluded unless explicitly reversed:

- emailing/transmitting documents
- automatic calls/messages/contact
- OCR/image recognition
- document upload/storage/attachments
- giant/separate BOL dashboard/analytics portal
- Total Revenue/financial analytics
- fuzzy/name/unit/truck/leader/probabilistic matching
- complex escalation/routing/approval logic
- browser/WebView/local server/Node/cloud/helper processes

Do not add placeholder architecture for excluded capabilities.

## Honest limitations

- Unmatched items cannot enter driver-owned work/Handoff until exact durable Driver Code exists in WAA.
- Manual reassignment is intentionally absent because it would bypass the exact-code contract.
- A representative low-end office-PC benchmark remains separate from GitHub-hosted Windows validation.
- Maintenance and DOT workflows remain unimplemented and require separate evaluation.
