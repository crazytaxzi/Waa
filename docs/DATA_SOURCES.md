# WAA Data Sources

This document defines what the supplied reports actually support. Imported report data is evidence and context; it must not silently replace local work or conversation history.

## 1. Rolling 7 Day — primary roster and idle source

Expected file family:

`rolling 7 day_data*.csv`

The supplied representation is structurally accurate. Only the Driver Code, Driver Name, and Driver Leader values were obfuscated. The first row is the real header row and the remaining rows preserve the real repeated pattern.

### Confirmed driver field format

`Group by (copy)` contains:

`<DriverCode><whitespace><DriverName>`

Confirmed parsing rule:

1. Trim the complete cell.
2. The leading contiguous alphanumeric token is Driver Code.
3. Split at the first whitespace after that token.
4. The complete trimmed remainder is Driver Name, including any spaces in the name.
5. Normalize Driver Code to uppercase for comparison while retaining the raw source label for audit.
6. Reject a blank code, blank name, or code containing anything other than letters and digits.

Do not use fixed-position slicing. The stars in the supplied representation show the maximum permitted code width, not padding that should be included in the value. A shorter valid code still ends at the first whitespace.

`Driver Leader` is one complete leader code in its own column. The supplied template shows **ten asterisks** in this field, so the confirmed maximum is **10 alphanumeric characters**, not five. Trim it, validate it as alphanumeric with a maximum length of 10, normalize it to uppercase for comparison, and retain the raw source value for evidence. It is organizational context, never driver identity.

### Confirmed columns

- `Group by (copy)`
- `Measure Names`
- first `Week Start Date`
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
- repeated `Week Start Date`
- `Measure Values`

Header matching must trim a UTF-8 BOM, convert non-breaking spaces to normal spaces, and collapse repeated whitespace. The first occurrence of `Week Start Date` is the authoritative weekly/report-cycle field.

### Repeated row pattern

Each driver/week appears through two measure rows:

- `OOR %`
- `Idle %`

Driver identity, week, raw engine hours, raw idle hours, Unit Code, Driver Leader, terminal, fleet, cost center, and line of business repeat across those rows. WAA must normalize the pair into one weekly driver observation.

The companion rows must agree on all repeated operational fields. A disagreement is a source conflict and must be rejected or surfaced; WAA must never choose one side silently.

### Identity semantics

- Driver Code is the durable entity key.
- Driver Name is the current human-readable identity attached to Driver Code.
- Unit Code is an object/assignment observation and must be stored as text.
- Driver Leader is current organizational context.
- A Unit Code or Driver Leader change does not create another driver.
- A driver missing from a later roster does not lose historical work, conversation events, or observations.

### Report-cycle key

The maximum valid value from the first `Week Start Date` column becomes `ReportCycleDate`. The name is intentionally neutral about whether the source system treats the date as a beginning or ending boundary; WAA uses the supplied date as a stable cycle key.

### Weighted idle calculations

Driver 7-day:

`Idle7d = IdleHours7d / EngineHours7d × 100`

Driver 28-day:

`Idle28d = Sum(IdleHours for current, -7, -14, and -21 day periods) / Sum(EngineHours for those same periods) × 100`

Rules:

- calculate from raw hours, never by averaging weekly percentages
- require all four expected weekly observations for a complete 28-day result
- show incomplete coverage such as `3/4` when a period is absent
- a present zero-engine week still counts as present coverage
- a zero denominator displays `N/A`
- retain full precision and round only for display

Fleet 7-day:

`FleetIdle7d = Sum(Current Idle Hours) / Sum(Current Engine Hours) × 100`

Fleet 28-day:

`FleetIdle28d = Sum(Complete Driver 28-day Idle Hours) / Sum(Complete Driver 28-day Engine Hours) × 100`

Both fleet figures must expose included-driver coverage.

### Threshold relationship

The default threshold is `50.0%`, locally configurable, using strict greater-than comparison. A driver is above threshold when either the valid 7-day result or complete 28-day result exceeds the threshold. Incomplete 28-day coverage cannot qualify the driver by itself.

### Update policy

Rolling 7 Day is scanned/imported only:

1. once during application launch
2. when the user selects `Update Reports`

There is no continuous watcher or recurring polling. Each update resolves the actual Windows Downloads known folder, finds matching candidates, reads a stable source, validates it, hashes it, parses into temporary memory, normalizes repeated rows, calculates snapshots, and commits atomically. Any failure retains the last known-good roster.

## 2. Local idle-conversation events

Imported reports own metrics and roster context. WAA local events own whether contact occurred.

Conversation state is keyed by:

- Driver Code
- Report Cycle Date

Supported recorded outcomes:

- `Attempted`
- `Spoke`
- `Spoke — Follow-up`

No event means `Not Contacted` for that cycle. Same-cycle corrected reports preserve conversation events. A later cycle derives a fresh state without deleting prior history.

Each event snapshots the timestamp, 7-day value, 28-day value and coverage, threshold, Unit Code, Driver Leader, and source import ID.

## 3. Future sources

### Order Details Missing BOL

`Last Dispatch Driver cd` is the natural join candidate to Driver Code. `Last Dispatch Driver nm` is supporting evidence. Unmatched codes remain visible rather than being merged by fuzzy name matching.

### Detail (Miles)

This is an asset/service source with Driver Leader ownership context. It does not contain trustworthy specific-driver identity and must not be forced into a driver record.

### DOT Table

This is trailer-centric data with repeated measure rows. It must be normalized by trailer and remain separate from driver identity unless another source establishes a trustworthy relationship.

## 4. Universal import rules

- source files are read-only inputs
- never modify or delete the Downloads copy
- update only at launch or explicit `Update Reports`
- validate required headers before mutation
- retain source file metadata and SHA-256
- parse into temporary memory first
- commit valid imports atomically
- preserve last known-good data on rejection
- report the actual missing, malformed, or conflicting field
- store unit, trailer, and asset identifiers as text
- never create identity through truck assignment, Driver Leader, or fuzzy name matching
- never average percentages when raw numerator and denominator values are available
