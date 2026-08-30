# WAA Missing BOL Workflow v0.3

This document is the authoritative product and technical specification for the compact Missing BOL workflow. It defines the source contract, exact-code matching, import lifecycle, local statuses/actions, linked work, queue behavior, handoff integration, unmatched handling, and permanent scope boundaries.

## Purpose and scope

Missing BOL is a small local work queue integrated into WAA’s existing driver queue, selected-driver pane, unified work history, and deterministic handoff.

It is not a document repository, communication system, OCR tool, analytics dashboard, revenue report, matching engine, or escalation platform. WAA never sends a request, calls a driver, stores a document attachment, reads an image, or guesses an identity.

## Runtime source contract

Expected Windows Downloads filename family:

`Order Details Missing BOL*.xlsx`

Accepted examples include:

- `Order Details Missing BOL.xlsx`
- `Order Details Missing BOL (1).xlsx`
- `Order Details Missing BOL-new.xlsx`

Temporary Office lock files beginning with `~$` are ignored.

The runtime workbook is a read-only source. WAA never modifies, renames, moves, deletes, rewrites, or saves over it.

## XLSX reader boundary

WAA uses a bounded managed ZIP/XML reader. It requires no Excel installation, Office, COM automation, Internet access, administrator access, browser runtime, or helper process.

Supported cell representations:

- shared strings
- inline strings
- ordinary strings/formula-result strings
- numeric cells
- zero-padded numeric identifiers according to workbook number format
- Excel serial dates
- blank cells

Numeric identifiers are converted to full invariant text without scientific notation. Leading zeros are preserved when the workbook’s cell format supplies them. Driver codes and order identifiers are never durable integers.

## Worksheet and header selection

WAA examines worksheets in workbook order. It chooses the first worksheet whose first non-empty row contains all required headers. The sheet name is not hard-coded.

Header normalization:

1. remove a BOM if present
2. convert non-breaking spaces to ordinary spaces
3. trim outer whitespace
4. collapse repeated internal whitespace
5. compare case-insensitively

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

Repeated irrelevant `Terminal Leader` columns are tolerated. A required header repeated after normalization is ambiguous and rejects the workbook. `Driver Leader` remains distinct from `Terminal Leader`.

WAA does not require or use Total Revenue, billing staff, AR staff, Buyer, Carrier, Dray Name, or duplicate Terminal Leader columns.

## Row validation and source-item identity

`Order #` is the durable source-item key.

- Trim source text.
- Normalize uniqueness as uppercase-invariant exact text.
- Preserve source display text.
- Blank Order # rejects the complete workbook.
- Truly identical repeated rows collapse to one item.
- Conflicting rows for one normalized Order # reject the complete workbook and identify the source order/rows.

`Empty Call Date` is required and stored as a date-only value. WAA accepts Excel numeric dates, serial dates, `M/d/yy`, `M/d/yyyy`, zero-padded equivalents, and ISO `yyyy-MM-dd`. Invalid values reject the workbook and identify the Order #, worksheet, and cell.

Origin, destination, Bill To, miles, source name, source code, and other context may be blank. A blank/unknown source Driver Code does not invalidate the order; it makes the item unmatched.

All rows are parsed and validated into temporary memory before database mutation. WAA never partially imports a structurally invalid workbook.

## Exact Driver Code matching

The only matching rule is:

`TrimUpper(Last Dispatch Driver cd) == TrimUpper(Driver Code)`

Both sides remain text. Meaningful leading zeros are preserved. Matching uses all durable driver entities already known to WAA, not only the current roster.

WAA never matches Missing BOL by:

- Driver Name
- Unit Code
- truck assignment
- Driver Leader
- source leader
- substring
- prefix
- punctuation cleanup/invention
- name similarity
- fuzzy/probabilistic logic
- manual automatic association

`Last Dispatch Driver nm` is supporting evidence only. When exact Driver Code matches but the source name differs, WAA keeps the code match, preserves both names, displays a restrained data-quality note, and does not overwrite durable Driver Name or split the driver.

## Unmatched items

A blank or unknown normalized source Driver Code remains unmatched.

WAA preserves the item and exposes a compact same-window read-only list containing:

- Order #
- Empty Call Date
- source Driver Code
- source Driver Name
- route
- latest-source presence indicator when applicable

Unmatched items create no driver-owned task, do not affect driver priority, and do not enter handoff. They cannot be locally assigned through a guessed identity.

After a later Rolling 7 Day import introduces the exact Driver Code, the existing item is attached automatically by exact code and its linked task is created once. Repeating the attachment check is idempotent.

## Database model and migration

Missing BOL uses separate non-destructive tables:

- `missing_bol_imports` — accepted workbook metadata/hash/time/row count
- `missing_bol_items` — source evidence, exact association, local status, presence, resolution, task link
- `missing_bol_action_events` — append-only local action history and linked activity entry
- `missing_bol_work_links` — task/action work provenance, item ID, and source import ID

Indexes support normalized Order #, normalized source Driver Code, matched-driver unresolved reads, current status, Empty Call Date, latest-source presence, action history, aggregate counts, and task/action links.

Migration occurs after the existing base work schema initialization and advances the database schema version without deleting existing tables/data. A migration failure is logged and surfaced at startup. WAA never creates a replacement database behind the user’s back.

## Accepted workbook lifecycle

Each accepted workbook is a source snapshot:

1. resolve the actual Downloads known folder
2. enumerate matching non-lock `.xlsx` files newest-last-write first
3. stable-read a candidate
4. hash complete bytes with SHA-256
5. skip an already accepted hash
6. parse and validate completely in memory
7. begin one SQLite transaction
8. insert the import record
9. mark all prior items not present in the newest source
10. upsert every row by normalized exact Order #
11. update source context and last-seen values
12. preserve local status, notes, resolution, task, and action history
13. match exact durable Driver Codes
14. create a task only for a matched unresolved item without one
15. commit the complete snapshot

A database error rolls back the complete snapshot.

## Source disappearance and return

Disappearance from a later accepted workbook never resolves or deletes an item.

For an unresolved absent item:

- keep local status
- keep the same open task
- mark `is_present_in_latest_import = false`
- display `Not in latest report`

For a resolved item that appears again:

- keep Resolved and its timestamp
- keep the same resolved task
- mark it present again
- display `Resolved — present again in latest report`
- offer Reopen
- never reopen or contact automatically

If a later workbook places an existing normalized Order # under a different normalized source Driver Code, WAA rejects the new snapshot rather than moving driver-owned work/history. The prior accepted source/item/task/action state remains intact.

## Linked task behavior

A newly matched unresolved item owns exactly one linked open FollowUp work entry (`MissingBolTask`).

Task text uses available source context:

`Missing BOL for order SYN1001, empty call 8/27/2026, Boise, ID → Auburn, WA. Status: Open.`

The task snapshots matched Driver Code, Unit Code, Driver Leader, report cycle when available, source import, and creation UTC.

Reimport updates source context/status wording in the same task. It does not create another task. Resolve sets the task’s resolution timestamp. Reopen clears that timestamp on the same task.

The task appears once in Open Work and once in Needs Follow-up. It is not duplicated by separately rendering the BOL item/event table.

General Open Work Resolve/Reopen is intentionally unavailable for MissingBolTask. The UI directs the user to the Missing BOL controls, and a database guard rejects bypass resolution. This prevents item/task drift.

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

- set status Resolved and resolved UTC
- resolve linked task at the same UTC
- append action event
- create completed activity: `Resolved missing BOL for order …`

### Reopen

- set status Open and clear resolved UTC
- reopen the same linked task
- append action event
- create completed activity: `Reopened missing BOL for order …`

Optional notes are trimmed and appended once to activity text while remaining in action history. Old events are never overwritten.

## Action atomicity and retry behavior

Requested, Attempted, and Follow-up commit item status, task text/state, action event, and completed activity together.

Resolved commits item resolution, task resolution, action event, and completed activity together.

Reopen commits item reopen, same-task reopen, action event, and completed activity together.

Any failure rolls back all of those changes. Item-level commands disable duplicate submission while saving. A failed save preserves typed note text for retry.

## Queue and search integration

Fleet rows show:

- Open Work: all unresolved work
- BOL: unresolved matched Missing BOL subset

Counts and oldest dates are loaded through aggregate indexed reads, not one query per driver/order.

Priority remains:

1. unfinished high-idle contact: Spoke — Follow-up, Attempted, Not Contacted
2. above-threshold Spoke, unresolved work before clear
3. remaining unresolved work, including MissingBolTask
4. clear drivers

Missing BOL cannot bury unfinished high-idle work. Within otherwise equal ordinary unresolved drivers, older Empty Call Date may sort first; Driver Name and Driver Code remain stable tie-breakers.

Search includes attached Order # text through deterministic substring matching. `Next Needing Attention` respects visible results and naturally selects a driver whose only issue is Missing BOL.

## Selected-driver presentation

The selected-driver pane remains in the main window and preserves this order:

1. identity/idle context
2. idle-contact controls
3. Missing BOL
4. Open Work
5. New Work
6. Today’s Activity
7. Next Needing Attention

Missing BOL cards support multiple orders and show unresolved first. Each card displays Order #, Empty Call Date, route, status, optional customer/miles, exact source code/name, presence/name warning, optional note, and direct actions. The list is scroll-bounded so the window does not grow without limit.

All surfaces use dynamic light/dark resources and word labels. There is no decorative animation, gradient, glow, dashboard tile, or modal workflow maze.

## Work history and handoff

MissingBolTask appears in Open Work and `NEEDS FOLLOW-UP` while unresolved.

MissingBolAction appears in Today’s Activity and `COMPLETED TODAY` when created during the current local day.

A resolved task is excluded from Completed Today so the Resolved action is the one completion line. Reopen returns the same task to Needs Follow-up and adds one Reopened action line to Completed Today.

Handoff lines use saved task/action text and snapshot Unit Code context. Editing/copying the draft never changes BOL items, tasks, events, source state, reports, or identity. Regenerate intentionally rebuilds from current saved work.

## Report-update independence

Rolling 7 Day and Missing BOL are scanned only:

1. once at launch
2. through explicit `Update Reports`

Each source imports independently. A valid Rolling update may commit while BOL fails; a valid BOL update may commit while Rolling is missing/current/invalid. The combined status identifies both outcomes and marks partial failure honestly.

A missing BOL workbook reports `No Missing BOL workbook found` and does not mark known items absent. An accepted hash reports `Missing BOL already current`. A locked/partial/invalid workbook preserves the last accepted BOL state.

There is no FileSystemWatcher, timer, polling, recurring scan, or automatic mid-session refresh.

## Privacy and permanent exclusions

The public repository contains only synthetic BOL workbooks/items, driver identities, routes, customers, orders, databases, and logs. Production reports/data are runtime read-only inputs and are never committed.

Permanently excluded unless explicitly reversed in a later bounded milestone:

- emailing or transmitting documents
- automatic calls/messages/contact
- OCR or image recognition
- document upload/storage/attachments
- giant or separate BOL dashboard/analytics portal
- Total Revenue or financial analytics
- fuzzy/name/unit/truck/leader/probabilistic matching
- complex escalation/routing/approval logic
- browser/WebView/local server/Node/cloud/helper processes

Do not add placeholder architecture for excluded capabilities.

## Honest limitations

- Unmatched items cannot enter driver-owned work or handoff until the exact durable Driver Code exists in WAA.
- v0.3 does not provide manual reassignment because that would bypass the exact-code contract.
- A representative low-end office-PC benchmark remains separate from GitHub-hosted Windows validation.
- Maintenance and DOT workflows remain unimplemented and require separate evaluation.
