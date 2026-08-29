# WAA Data Sources

This document defines only what the supplied reports actually support. It does not treat unrelated report fields as identity evidence merely because they are convenient.

## 1. Rolling 7 Day — primary roster and idle source

Expected file family:

`rolling 7 day_data*.csv`

The supplied representation contains these columns:

- `Group by (copy)`
- `Measure Names`
- `Week Start Date`
- `[Rolling 7 Day Engine Time]/60`
- `[Rolling 7 Day Idle Time]/60`
- `Rolling 7 Day Dispatch Miles`
- `Rolling 7 Day Qualcomm Miles`
- `Cost Center`
- `Driver Leader`
- `Driver Terminal`
- `Fleet Leader`
- `OPS LOB`
- `Rolling 7 Day Start Date`
- `Unit Code`
- duplicate `Week Start Date`
- `Measure Values`

### What WAA needs from this source

Identity and roster:

- combined driver label from `Group by (copy)`
- `Driver Leader`
- `Unit Code`
- `Driver Terminal`
- `Fleet Leader`
- `Cost Center`
- `OPS LOB`
- normalized weekly/report-cycle date

Idle calculations:

- `[Rolling 7 Day Engine Time]/60`
- `[Rolling 7 Day Idle Time]/60`
- `Measure Names` for source-shape validation
- `Measure Values` only as comparison/evidence, not as the authoritative basis for weighted calculations

WAA derives weighted percentages from raw engine and idle hours. It must not average already-calculated weekly percentages.

### Important shape of the supplied sample

The supplied representation has:

- 60 data rows
- 3 driver labels
- 10 weekly periods
- 2 measure rows per driver/week: `OOR %` and `Idle %`

Therefore a simple row-per-driver import is wrong. WAA must normalize repeated measure rows into one weekly driver observation.

The duplicated rows should agree on driver label, week, raw engine hours, raw idle hours, Unit Code, Driver Leader, and other roster context. A disagreement is a data-quality conflict and must be surfaced rather than silently resolved.

### Driver identity limitation in the supplied representation

The uploaded representation masks the actual driver-code/name text. It visibly shows a combined driver label, but the exact delimiter between Driver Code and Driver Name cannot be proven from the redacted sample.

Implementation rule:

- store the raw driver label
- create parser tests against an actual unredacted export before locking the split rule
- reject any row whose label cannot be parsed unambiguously
- never derive identity from Unit Code, Driver Leader, or fuzzy name matching

### Identity semantics

- Driver Code is the durable entity key.
- Driver Name is the identity display value associated with Driver Code.
- Unit Code is an associated object/observation, not identity.
- Driver Leader is current organizational context, not identity.
- A changed Unit Code does not create a new driver.
- A missing driver in a later roster does not erase prior work or conversation history.

### Report-cycle key

The accepted file may contain multiple weekly dates. The maximum normalized value from the report's `Week Start Date` field becomes `ReportCycleDate` for current-roster and idle-conversation purposes.

WAA does not assume the export's label accurately describes whether this is semantically the beginning or ending date. It uses the supplied date as the stable cycle key.

### Weighted driver 7-day calculation

For the current weekly observation:

`Idle7d = IdleHours7d / EngineHours7d × 100`

Rules:

- use raw source hours
- negative hours invalidate the observation
- zero engine hours produces `N/A`
- retain full precision and round only for display

### Weighted driver 28-day calculation

Use the current `ReportCycleDate` plus the expected periods exactly 7, 14, and 21 days earlier:

`Idle28d = Sum(IdleHours for all 4 expected periods) / Sum(EngineHours for all 4 expected periods) × 100`

Rules:

- never average weekly percentages
- require all four expected observations for a complete result
- show incomplete coverage such as `3/4` if a period is missing
- a present zero-engine week remains a present period
- zero total engine hours produces `N/A`

### Fleet weighted calculations

Current fleet 7-day:

`FleetIdle7d = Sum(Current Idle Hours) / Sum(Current Engine Hours) × 100`

Include only observations with a valid positive denominator and expose the included-driver count.

Current fleet 28-day:

`FleetIdle28d = Sum(Complete 28-day Idle Hours) / Sum(Complete 28-day Engine Hours) × 100`

Include only current-roster drivers with all four expected periods and a positive total denominator. Show coverage as eligible/current drivers.

### Threshold relationship

The application has a locally configurable threshold, default `50.0%`, using a strict greater-than comparison.

A driver is above threshold when either:

- valid current weighted 7-day idle exceeds the threshold, or
- complete current weighted 28-day idle exceeds the threshold

Incomplete 28-day coverage cannot independently qualify a driver, but a valid 7-day value still can.

### Update policy

Rolling 7 Day is scanned/imported only:

1. once on application launch
2. when the user selects `Update Reports`

There is no continuous watcher or polling loop. An added or corrected Downloads file remains untouched until one of those two update paths runs.

Each update:

- resolves the actual Windows Downloads known folder
- scans candidate files
- validates source headers and shape
- reads the selected stable file
- hashes the content
- skips an already accepted hash
- parses into a temporary model
- normalizes duplicate measure rows
- calculates current weighted snapshots
- commits the complete import atomically
- retains last known-good data on any failure

## 2. Local idle-conversation events — authoritative user work

Imported reports determine metrics and current roster context. They do not determine whether the user spoke with a driver.

WAA local conversation events are authoritative for that fact and are keyed by:

- Driver Code
- Report Cycle Date

Supported outcomes:

- `Attempted`
- `Spoke`
- `Spoke — Follow-up`

No event means `Not Contacted` for that cycle.

Each event snapshots the 7-day value, 28-day value/coverage, threshold, Unit Code, Driver Leader, source import ID, timestamp, and optional note/follow-up. Same-cycle report corrections do not erase these events. A later report cycle creates a fresh derived current state while preserving all prior events.

## 3. Order Details Missing BOL — future driver work source

Supplied workbook: `Order Details Missing BOL.xlsx`

Useful fields observed:

- `Order #`
- `Empty Call Date`
- `Origin City St`
- `Destination City St`
- `Rev Type`
- `Terminal `
- `Driver Leader `
- `Driver Status`
- `Last Dispatch Driver cd`
- `Last Dispatch Driver nm`
- `Loaded Miles`
- `Order Level Order Miles`
- `Total Revenue`

### Relationship to the driver entity

`Last Dispatch Driver cd` is the natural join candidate to WAA Driver Code. `Last Dispatch Driver nm` is supporting display/evidence.

Do not use a truck, order number, Driver Leader, or Terminal Leader to manufacture driver identity.

This source may eventually create or refresh Missing-BOL work context attached to an existing driver. If it contains a driver code not present in the roster, retain the item as unmatched instead of auto-merging by name.

It must follow the same launch/manual update policy unless the user explicitly changes that product rule.

## 4. Detail (Miles) — future maintenance/asset source

Supplied file: `Detail (Miles)_data.csv`

Observed fields:

- `Asset #`
- `Service Type`
- `Terminal`
- `Cost Center`
- `Fleet Leader Code`
- `Driver Leader Code`
- `Due Date`
- `Service Status`
- `Measure Names`
- `COLOR`
- `Measure Values`
- sort-helper fields

The supplied file has 17 rows representing 13 distinct assets and three Driver Leader codes.

### Relationship to the driver model

This report does **not** provide a reliable driver identity field. It is an asset/service queue with Driver Leader ownership context.

Do not attach a maintenance item to a specific driver merely because a driver currently uses an asset unless a future source explicitly supports that relationship at the relevant time.

A future maintenance view should likely be leader/asset oriented and may sit beside the driver workflow rather than inside every driver card.

## 5. DOT Table — future trailer source

Supplied file: `DOT Table_data(1).csv`

Observed fields:

- `Trailer`
- `Statuses`
- `Description`
- `Last DOT Date`
- `Responsible CSR`
- `Responsible CSR Supervisor`
- `T2 Date`
- `Customer`
- `KMA`
- `Measure Names`
- `Measure Values`

The supplied file has 122 rows for 61 distinct trailers. Each trailer appears through repeated measure rows such as `Trailer Count` and `Days Since Last DOT`.

### Relationship to the driver model

This is trailer-centric data, not driver identity data.

A future DOT queue must deduplicate repeated measure rows by trailer and preserve trailer status/customer context. It should not be forced under a driver unless another source establishes a trustworthy current relationship.

## 6. Source precedence

1. **Rolling 7 Day** owns the current driver roster, current driver/unit/leader observation, and raw idle-hour evidence.
2. **WAA calculation rules** own weighted 7-day/28-day values and coverage state.
3. **WAA local idle-conversation events** own whether the user attempted or completed the weekly conversation.
4. **WAA local work entries** own what the user did, what is waiting, and what needs follow-up.
5. Other reports may later add work context, but they do not overwrite local work/conversation history.
6. No imported report deletes historical drivers, work entries, conversation events, or prior observations because a row disappears from a newer export.

## 7. Import safety rules

All source importers should follow the same contract:

- source files are read-only inputs
- never modify or delete the Downloads copy
- update only at launch or explicit `Update Reports`
- validate required headers before mutation
- store import metadata and content hash
- parse into a temporary in-memory model first
- commit a valid import atomically
- preserve last known-good data on rejection
- report errors with the actual missing/invalid field
- treat unit/trailer/asset numbers as text to preserve leading zeros
- never create identity through fuzzy name matching without an explicit reviewed rule
- never average percentages where raw numerator/denominator values are available
