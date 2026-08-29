# WAA Data Sources

This document defines only what the supplied reports actually support. It does not treat unrelated report fields as identity evidence merely because they are convenient.

## 1. Rolling 7 Day — primary roster source

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

### What WAA needs initially

The roster requires only:

- combined driver label from `Group by (copy)`
- `Driver Leader`
- `Unit Code`
- `Driver Terminal`
- `Fleet Leader`
- `Cost Center`
- `OPS LOB`
- newest `Week Start Date`

### Important shape of the supplied sample

The supplied representation has:

- 60 data rows
- 3 driver labels
- 10 weekly periods
- 2 measure rows per driver/week: `OOR %` and `Idle %`

Therefore a simple row-per-driver import would be wrong. WAA must normalize the repeated measure rows before building the roster.

### Driver identity limitation in the supplied representation

The uploaded representation masks the actual driver-code/name text. It visibly shows a combined driver label, but the exact delimiter between Driver Code and Driver Name cannot be proven from the redacted sample.

Implementation rule:

- store the raw driver label
- create a parser test against an actual unredacted export before locking the split rule
- if the label cannot be parsed unambiguously, reject that row rather than guessing

### Identity semantics

- Driver Code is the entity key.
- Driver Name is identity display data associated with Driver Code.
- Unit Code is an associated object/observation, not identity.
- Driver Leader is current organizational context, not identity.
- A changed Unit Code does not create a new driver.
- A missing driver in a later roster does not erase prior work history.

## 2. Order Details Missing BOL — future driver work source

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

This source should eventually create or refresh Missing-BOL work context attached to an existing driver. If the report contains a driver code not yet present in the roster, retain the item as unmatched instead of auto-merging based only on name.

## 3. Detail (Miles) — future maintenance/asset source

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

## 4. DOT Table — future trailer source

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

A future DOT queue must deduplicate the repeated measure rows by trailer and preserve trailer status/customer context. It should not be forced under a driver unless another source establishes a trustworthy current relationship.

## 5. Source precedence

For the initial product:

1. **Rolling 7 Day** owns the current driver roster and current driver/unit/leader observation.
2. **WAA local work entries** own what the user did, what is waiting, and what needs follow-up.
3. Other operational reports may later add work context, but they do not overwrite WAA work history.
4. No imported report deletes historical drivers, work entries, or prior observations merely because a row disappears from a newer export.

## 6. Import safety rules

All source importers should eventually follow the same basic contract:

- source files are read-only inputs
- never modify or delete the Downloads copy
- validate required headers before mutation
- store source import metadata and content hash
- parse into a temporary in-memory model first
- commit a valid import atomically
- preserve last known-good data on rejection
- report errors with the actual missing/invalid field
- treat unit/trailer/asset numbers as text to preserve leading zeros
- never create identity through fuzzy name matching without an explicit reviewed rule
