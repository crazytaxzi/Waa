# WAA Data Sources

This document defines what the supported reports establish. Imported/report data is source evidence and operational context; it must not silently replace saved work, conversation history, or durable driver identity.

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

`Driver Leader` is one complete code in its own column. The confirmed maximum is **10 alphanumeric characters**. Trim it, validate it, normalize it to uppercase for comparison, and retain source context. It is organizational context, never driver identity.

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
- Unit Code is assignment/context stored as text.
- Driver Leader is organizational context.
- Unit or leader changes do not create another driver.
- A driver missing from a later roster does not lose saved work/contact history.

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

Each update resolves the real Windows Downloads known folder, finds matching candidates, reads a stable source, validates it, hashes it, parses to temporary memory, normalizes repeated rows, calculates snapshots, and commits atomically. Any failure retains the last-known-good saved roster.

## 2. Order Details Missing BOL — current source-only report

Expected file family:

`Order Details Missing BOL*.xlsx`

Examples include the base name, Windows numbered copies, and suffixed names. Temporary Office lock files beginning with `~$` are ignored.

WAA reads XLSX locally through a managed read-only ZIP/XML implementation. It does not require Microsoft Excel, Office, COM automation, Internet access, administrator rights, or a helper process.

### Source ownership

The current accepted workbook is the complete Missing BOL source of truth.

WAA does **not** persist current Missing BOL workbook rows, source hashes, item state, actions, notes, resolution state, action history, or generated BOL tasks to SQLite.

At each accepted launch/manual scan, WAA replaces the in-memory Missing BOL view with exactly the validated rows from that workbook. If a row is no longer in the workbook, it is no longer shown after that scan.

### Worksheet and header discovery

WAA does not rely on a specific worksheet name. It examines worksheets in workbook order and chooses the first worksheet whose first non-empty row contains every required header.

Header normalization:

1. remove a UTF-8 BOM when present
2. convert non-breaking spaces to ordinary spaces
3. trim leading/trailing whitespace
4. collapse repeated internal whitespace
5. compare case-insensitively

Repeated irrelevant `Terminal Leader` headers are tolerated. Any required header that appears more than once after normalization is ambiguous and rejects the workbook.

### Required source fields

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

WAA does not require or use Total Revenue, billing/AR personnel, Buyer, Carrier, Dray Name, or duplicate Terminal Leader values. It produces no financial summaries.

### Supported XLSX cell forms

- shared strings
- inline strings
- ordinary string/formula-result cells
- numeric cells
- numeric identifiers displayed with zero-padding formats
- date-formatted or ordinary Excel serial numbers
- blank cells

Numeric identifiers are rendered as full text rather than scientific notation. Meaningful leading zeros are preserved when the workbook number format supplies them. Identifier-like values are never converted into durable numeric identity.

### Order identity and row validation

`Order #` is the normalized current source-row identity used for workbook duplicate validation and stable in-session routing.

- trim source text
- normalize uniqueness through uppercase-invariant exact text
- preserve source display text
- blank Order # rejects the complete workbook
- truly identical repeated rows collapse to one current row
- conflicting repeated rows reject the complete workbook and identify the order/rows

`Empty Call Date` is required and represented as `DateOnly` in memory.

Accepted forms include real Excel numeric dates, Excel serial dates, `M/d/yy`, `M/d/yyyy`, zero-padded equivalents, and ISO `yyyy-MM-dd`.

An invalid date rejects the complete workbook. Origin, destination, Bill To, miles, and other context may be blank when Order # and Empty Call Date are valid.

The complete workbook is parsed and validated in temporary memory before replacing the current in-memory BOL view.

### Driver-code precedence and exact current-roster matching

The only driver association rule is:

`Normalize(Last Dispatch Driver cd) == Normalize(current Driver Code)`

Normalization is trim + uppercase invariant text comparison. Codes remain text and preserve leading zeros.

Field precedence:

1. Rolling 7 Day Driver Code owns durable driver identity.
2. Rolling 7 Day Driver Name remains WAA display identity.
3. Missing BOL `Last Dispatch Driver cd` is exact current-row association evidence only.
4. Missing BOL `Last Dispatch Driver nm` is supporting source evidence only.
5. Missing BOL source Driver Leader is source context only and never driver identity.

An exact code match remains valid when source name differs. WAA preserves both names, shows a restrained data-quality note, and does not overwrite durable Driver Name. There is no name, Unit Code, truck, Driver Leader, prefix, substring, similarity, fuzzy, probabilistic, or manual matching.

Blank or unknown source Driver Codes remain unmatched. They are shown read-only with Order #, date, source code/name, and route. They create no driver-owned work and do not enter Handoff. A later scan may match the row if the current roster then contains the exact code; there is no persistent attach operation.

### Current snapshot lifecycle

At launch and explicit `Update Reports`:

1. enumerate matching XLSX candidates newest first
2. stable-read candidate bytes
3. SHA-256 hash the complete bytes for same-session change detection
4. parse and validate fully in memory
5. replace the current in-memory BOL snapshot with the accepted rows
6. derive current matched/unmatched presentation from the current durable roster

The Missing BOL hash is not persisted. Restarting WAA starts with no BOL rows until the workbook is scanned again.

If no matching workbook exists, current Missing BOL rows are cleared. If all candidates are invalid, WAA reports the failure and does not repopulate old BOL rows from SQLite.

### Fleet/work/Handoff meaning

Current matched BOL rows may contribute:

- a visible per-driver/current overall BOL report count
- Order # search text
- `CURRENT MISSING BOL` read-only rows and detail
- a transient current-file `Missing BOLs:` section when Handoff is regenerated

They do not contribute:

- Open Work
- driver priority / `Needs Attention`
- `Next Work Item`
- Today’s Activity
- local BOL statuses/actions/notes/history

See `docs/MISSING_BOL_WORKFLOW.md` for the complete source-only behavior.

### Legacy upgrade data

Databases created by WAA v0.3–v0.4.5 may physically retain old `missing_bol_*` tables and generated BOL-linked work rows. v0.4.6 does not destructively drop them during normal upgrade. They are dormant compatibility data and are excluded from current Missing BOL state, Open Work, Today’s Activity, current priority, and current Handoff narrative.

Fresh v0.4.6 databases do not create Missing BOL tables.

### Missing BOL update policy

Missing BOL is scanned only at the same two explicit report-update boundaries as Rolling 7 Day: once at launch and through `Update Reports`. There is no watcher, polling, recurring scan, or automatic mid-session refresh.

Rolling 7 Day and Missing BOL update independently. Rolling persistence is not replaced by the source-only BOL model.

## 3. Local idle-conversation events

Imported Rolling 7 Day owns metrics and roster context. WAA local events own whether idle contact occurred.

Conversation state is keyed by Driver Code + Report Cycle Date. Supported recorded outcomes are Attempted, Spoke, and Spoke — Follow-up; no event means Not Contacted. Same-cycle corrected reports preserve events. A later cycle derives fresh pending state without deleting prior history.

Each event snapshots timestamp, 7-day value, 28-day value/coverage, threshold, Unit Code, Driver Leader, and source import ID.

## 4. Saved manual work

WAA persists manual Done, Waiting, and FollowUp work plus the linked work generated by idle-contact events. Current Missing BOL rows do not create saved work.

See `docs/WORK_LOG_HANDOFF.md`.

## 5. Future evaluated sources

### Detail (Miles)

This is an asset/service source with Driver Leader ownership context. It does not contain trustworthy specific-driver identity and must not be forced into a driver record.

### DOT Table

This is trailer-centric data with repeated measure rows. It must be normalized by trailer and remain separate from driver identity unless another source establishes a trustworthy relationship.

Neither maintenance nor DOT workflow is implemented in v0.4.6.

## 6. Universal source/import rules

- source files are read-only inputs
- never modify, rename, move, delete, or overwrite the Downloads copy
- update/scan only at launch or explicit `Update Reports`
- validate required headers before accepting source data
- parse into temporary memory first
- Rolling persisted imports remain hashed/idempotent/atomic and preserve last-known-good saved roster
- Missing BOL is source-only/current-session and never restored from SQLite
- report actual missing, malformed, ambiguous, or conflicting fields
- store driver, unit, trailer, order, and asset identifiers as text where persisted
- never create identity through truck assignment, Driver Leader, source name, or fuzzy matching
- never average percentages when raw numerator and denominator values exist
- never commit production source data to the public repository