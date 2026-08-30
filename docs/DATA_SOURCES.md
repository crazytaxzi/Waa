# WAA Data Sources

This document defines what the supported reports actually establish. Imported report data is source evidence and operational context; it must not silently replace local work, conversation, Missing BOL status, or action history.

## 1. Rolling 7 Day — primary roster and idle source

Expected file family:

`rolling 7 day_data*.csv`

### Driver field format

`Group by (copy)` contains:

`<DriverCode><whitespace><DriverName>`

Parsing rule:

1. Trim the complete cell.
2. The leading contiguous alphanumeric token is Driver Code.
3. Split at the first whitespace after that token.
4. The complete trimmed remainder is Driver Name, including spaces.
5. Normalize Driver Code to uppercase for comparison while retaining the raw source label.
6. Reject a blank code, blank name, code longer than six characters, or code containing anything other than letters and digits.

Do not use fixed-position slicing. A shorter valid code ends at the first whitespace.

`Driver Leader` is one complete code in its own column. The confirmed maximum is **10 alphanumeric characters**, not five. Trim it, validate it, normalize it to uppercase for comparison, and retain the raw source value as evidence. It is organizational context, never driver identity.

### Required columns

- `Group by (copy)`
- `Measure Names`
- first `Week Start Date`
- `[Rolling 7 Day Engine Time]/60`
- `[Rolling 7 Day Idle Time]/60`
- `Cost Center`
- `Driver Leader`
- `Driver Terminal`
- `Fleet Leader`
- `OPS LOB`
- `Unit Code`

Header matching trims a UTF-8 BOM, converts non-breaking spaces to ordinary spaces, trims outer whitespace, and collapses repeated internal whitespace. The first occurrence of `Week Start Date` is authoritative.

### Repeated row pattern

Each driver/week appears through two measure rows:

- `OOR %`
- `Idle %`

Driver identity, week, raw engine hours, raw idle hours, Unit Code, Driver Leader, terminal, fleet, cost center, and line of business repeat across both rows. WAA normalizes the pair into one weekly observation. Companion rows must agree on repeated operational fields; disagreement is a source conflict and is never silently resolved.

### Identity semantics

- Driver Code is the durable entity key.
- Driver Name is current display identity attached to Driver Code.
- Unit Code is an object/assignment observation stored as text.
- Driver Leader is organizational context.
- Unit or leader changes do not create another driver.
- A driver missing from a later roster does not lose history, work, contacts, or previously matched Missing BOL items.

### Report-cycle key

The maximum valid value from the first `Week Start Date` column becomes `ReportCycleDate`. WAA uses the supplied date as a stable cycle key without inventing source semantics.

### Weighted idle calculations

Driver 7-day:

`Idle7d = IdleHours7d / EngineHours7d × 100`

Driver 28-day:

`Idle28d = Sum(IdleHours for current, -7, -14, and -21 day periods) / Sum(EngineHours for those periods) × 100`

Rules:

- calculate from raw hours, never by averaging weekly percentages
- require all four expected weekly observations for a complete 28-day result
- show incomplete coverage such as `3/4` when a period is absent
- a present zero-engine week still counts as present coverage
- a zero denominator displays `N/A`
- retain full precision and round only for display

Fleet 7-day and 28-day values also use summed numerators/denominators and expose included-driver coverage. Fleet 28-day includes only current-roster drivers with all four expected observations and a positive total denominator.

### Threshold relationship

The default threshold is `50.0%`, locally configurable, using strict greater-than comparison. Either a valid 7-day result or a complete 28-day result above threshold places a driver in the high-idle population. Incomplete 28-day coverage cannot qualify a driver by itself.

### Update policy

Rolling 7 Day is scanned/imported only:

1. once during application launch
2. when the user selects `Update Reports`

Each update resolves the real Windows Downloads known folder, finds matching candidates, reads a stable source, validates it, hashes it, parses to temporary memory, normalizes repeated rows, calculates snapshots, and commits atomically. Any failure retains the last-known-good roster.

## 2. Order Details Missing BOL — exact-code work source

Expected file family:

`Order Details Missing BOL*.xlsx`

Examples include the base name, Windows numbered copies, and suffixed names. Temporary Office lock files beginning with `~$` are ignored.

WAA reads XLSX locally through a managed read-only ZIP/XML implementation. It does not require Microsoft Excel, Office, COM automation, Internet access, administrator rights, or a helper process.

### Worksheet and header discovery

WAA does not rely on a specific worksheet name. It examines worksheets in workbook order and chooses the first worksheet whose first non-empty row contains every required header.

Header normalization:

1. remove a UTF-8 BOM when present
2. convert non-breaking spaces to ordinary spaces
3. trim leading/trailing whitespace
4. collapse repeated internal whitespace
5. compare case-insensitively

The source may contain repeated irrelevant `Terminal Leader` headers. Those duplicates are tolerated because they are not required. Any required header that appears more than once after normalization is ambiguous and rejects the workbook with a clear validation error.

### Required persisted fields

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

WAA does not require or persist Total Revenue, billing/AR personnel, Buyer, Carrier, Dray Name, or duplicate Terminal Leader values. It produces no financial summaries.

### Supported XLSX cell forms

- shared strings
- inline strings
- ordinary string/formula-result cells
- numeric cells
- numeric identifiers displayed with zero-padding formats
- date-formatted or ordinary Excel serial numbers
- blank cells

Numeric identifiers are rendered as full text rather than scientific notation. Meaningful leading zeros are preserved when the workbook’s number format supplies them. Identifier-like values are never converted into durable numeric identity.

### Order identity and row validation

`Order #` is the durable Missing BOL item key.

- Trim source text.
- Normalize uniqueness through uppercase-invariant exact text.
- Preserve source display text.
- Blank Order # rejects the complete workbook.
- Truly identical repeated rows for one normalized Order # collapse to one item.
- Conflicting rows for one normalized Order # reject the complete workbook and identify the order/rows.

`Empty Call Date` is required and stored as `DateOnly`.

Accepted forms include:

- real Excel numeric date values
- Excel serial dates
- `M/d/yy`
- `M/d/yyyy`
- zero-padded equivalents
- ISO `yyyy-MM-dd`

An invalid date rejects the complete workbook and identifies the Order #, worksheet, and cell. Origin, destination, Bill To, miles, and other context may be blank without losing a row when Order # and Empty Call Date are valid.

The entire workbook is parsed and validated in temporary memory before database mutation. A structural row error prevents partial import and preserves the last accepted BOL state.

### Driver-code precedence and exact matching

The only driver association rule is:

`Normalize(Last Dispatch Driver cd) == Normalize(Driver Code)`

Normalization is trim + uppercase invariant text comparison. Codes remain text and preserve leading zeros. WAA matches against all durable driver entities already stored, including historical/non-current drivers.

Field precedence:

1. Rolling 7 Day Driver Code owns durable driver identity.
2. Rolling 7 Day Driver Name remains WAA display identity.
3. Missing BOL `Last Dispatch Driver cd` is exact association evidence only.
4. Missing BOL `Last Dispatch Driver nm` is supporting source evidence only.
5. Missing BOL source Driver Leader is source context, not driver identity and not the task’s current organizational snapshot.
6. Task Unit Code/Driver Leader/report-cycle snapshots come from the durable WAA driver context at task creation.

An exact code match remains valid when source name differs. WAA preserves both names, shows a restrained data-quality note, and does not overwrite durable Driver Name. There is no name, Unit Code, truck, Driver Leader, prefix, substring, similarity, fuzzy, or probabilistic matching.

Blank or unknown source Driver Codes remain unmatched. They are preserved and shown read-only with Order #, date, source code/name, and route. They create no driver-owned task and do not enter handoff. If a later Rolling 7 Day import introduces the exact code, WAA attaches the item and creates its task exactly once.

### Accepted snapshot lifecycle

Each accepted workbook is an independent source snapshot:

1. stable-read the candidate
2. SHA-256 hash the complete bytes
3. skip an already accepted hash
4. parse and validate fully in memory
5. insert a Missing BOL import record
6. mark prior items absent from the newest source
7. upsert rows by normalized exact Order #
8. mark imported rows present and update last-seen/source context
9. preserve local status, resolution, task, notes, and action history
10. create tasks only for newly matched unresolved items without one
11. commit the complete snapshot in one transaction

Disappearance never resolves an item or its task. A resolved item that appears again remains resolved and is flagged as present again. A later source row that assigns an existing Order # to a different normalized source Driver Code is a conflict; the complete new snapshot is rejected and prior item/task/history remains unchanged.

### Missing BOL update policy

Missing BOL is scanned/imported only at the same two explicit report-update boundaries as Rolling 7 Day: once at launch and through `Update Reports`. There is no watcher, polling, recurring scan, or automatic mid-session refresh.

Rolling 7 Day and Missing BOL update independently. A successful source commits even if the other source is missing or invalid. The combined message identifies both outcomes and never falsely claims a full success after a partial failure.

## 3. Local idle-conversation events

Imported reports own metrics and roster context. WAA local events own whether contact occurred.

Conversation state is keyed by Driver Code + Report Cycle Date. Supported recorded outcomes are Attempted, Spoke, and Spoke — Follow-up; no event means Not Contacted. Same-cycle corrected reports preserve events. A later cycle derives fresh pending state without deleting prior history.

Each event snapshots timestamp, 7-day value, 28-day value/coverage, threshold, Unit Code, Driver Leader, and source import ID.

## 4. Local Missing BOL actions

The workbook owns source evidence. WAA local state owns Open, Requested, Attempted, FollowUp, and Resolved. Reopen is an action that returns a resolved item to Open.

Local action history is append-only. Requested/Attempted/Follow-up keep the item/task unresolved. Resolved resolves both. Reopen clears both resolution timestamps and reuses the same task. Each action creates one completed activity entry and saves atomically with item/task/event state.

## 5. Future evaluated sources

### Detail (Miles)

This is an asset/service source with Driver Leader ownership context. It does not contain trustworthy specific-driver identity and must not be forced into a driver record.

### DOT Table

This is trailer-centric data with repeated measure rows. It must be normalized by trailer and remain separate from driver identity unless another source establishes a trustworthy relationship.

Neither maintenance nor DOT workflow is implemented in v0.3.

## 6. Universal import rules

- source files are read-only inputs
- never modify, rename, move, delete, or overwrite the Downloads copy
- update only at launch or explicit `Update Reports`
- validate required headers before mutation
- retain source metadata and SHA-256
- parse into temporary memory first
- commit valid imports atomically
- preserve last-known-good source state on rejection
- report the actual missing, malformed, ambiguous, or conflicting field
- store driver, unit, trailer, order, and asset identifiers as text
- never create identity through truck assignment, Driver Leader, source name, or fuzzy matching
- never average percentages when raw numerator and denominator values exist
- never commit production source data to the public repository
