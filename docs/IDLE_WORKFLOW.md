# WAA Idle Workflow Specification

## 1. Purpose

The fleet list must answer four questions immediately:

1. What are this driver's weighted 28-day and 7-day idle percentages?
2. Is either value above the configured attention threshold?
3. Have I actually spoken with this driver about idle for the current report cycle?
4. Who needs my attention next without making me maintain a filter?

This workflow is part of the main driver work-through. It is not a separate dashboard or decorative analytics page.

## 2. Reporting-cycle key

The accepted Rolling 7 Day report can contain many weekly periods. WAA defines the **current report cycle** as the maximum valid date found in the report's `Week Start Date` field after normalization.

Internally this is stored as `ReportCycleDate`. The name does not assume whether the exporting system semantically treats that date as the beginning or ending boundary; it is simply the stable cycle key supplied by the report.

Idle conversation state is keyed by:

- `DriverCode`
- `ReportCycleDate`

A corrected or replaced report with the same `ReportCycleDate` must not erase conversation state. A report with a later `ReportCycleDate` naturally starts a new cycle while preserving all prior history.

## 3. Source normalization

The source repeats the same driver/week operational values across `OOR %` and `Idle %` measure rows. WAA must normalize those rows into one weekly observation per:

- Driver Code
- report cycle date

The repeated rows must agree on the raw engine hours, idle hours, Unit Code, Driver Leader, and other roster context. A conflict is a data-quality condition and must be surfaced instead of silently choosing one row.

Store raw hours at full source precision. Percentages are derived values, not identity or source keys.

## 4. Weighted calculations

### Driver weighted 7-day idle

For the current driver/week observation:

`Idle7d = IdleHours7d / EngineHours7d × 100`

Rules:

- negative engine or idle hours are invalid
- zero engine hours produces `N/A`
- retain full calculation precision in storage
- round only for display, initially to one decimal place

### Driver weighted 28-day idle

Use the current report cycle plus the three expected weekly periods exactly 7, 14, and 21 days earlier.

`Idle28d = Sum(IdleHours for 4 expected periods) / Sum(EngineHours for the same periods) × 100`

Rules:

- never average four weekly percentages
- require all four expected period observations for a complete 28-day value
- a zero-engine week may still be a present period and contributes zero to the denominator
- if any expected period is missing, show `Incomplete` with coverage such as `3/4`; do not present a normal 28-day percentage
- if total engine hours across all four periods is zero, show `N/A`

### Fleet weighted 7-day idle

For current-roster drivers with a valid current weekly denominator:

`FleetIdle7d = Sum(Current Idle Hours) / Sum(Current Engine Hours) × 100`

Show the included-driver count so missing/invalid coverage is visible.

### Fleet weighted 28-day idle

For current-roster drivers with complete four-period coverage:

`FleetIdle28d = Sum(Eligible 28-day Idle Hours) / Sum(Eligible 28-day Engine Hours) × 100`

Show coverage as `eligible drivers / current roster drivers`. Do not conceal incomplete coverage behind a confident fleet percentage.

## 5. Configurable threshold

- Default threshold: `50.0%`
- Comparison: strictly greater than (`>`), not greater-than-or-equal
- One threshold initially applies to both 7-day and 28-day percentages
- Valid input range: `0.0` through `100.0`
- Store the setting locally
- Changing the threshold immediately recomputes list priority from existing observations
- Changing the threshold never edits or deletes prior conversation records

A driver is **above threshold** when either:

- valid weighted 7-day idle is greater than the threshold, or
- complete weighted 28-day idle is greater than the threshold

An incomplete 28-day value cannot by itself put a driver above threshold, but a valid 7-day value still can.

## 6. Conversation state

Each driver/cycle has one current derived state backed by immutable events.

### Visible states

- `Not Contacted` — no idle contact event for the current cycle
- `Attempted` — an attempt was recorded but the driver was not reached
- `Spoke` — the idle conversation was completed for the current cycle
- `Spoke — Follow-up` — the driver was reached, but further action remains open

`Attempted` does not count as spoken. `Spoke — Follow-up` does count as spoken but remains actionable.

### Event fields

Each event records:

- event ID
- Driver Code
- report cycle date
- outcome
- timestamp
- optional concise note
- optional follow-up text/status
- weighted 7-day percentage snapshot
- weighted 28-day percentage and coverage snapshot
- configured threshold snapshot
- Unit Code snapshot
- Driver Leader snapshot
- source import ID

Snapshotting the context preserves what the user actually discussed even after later reports, truck changes, or threshold changes.

## 7. Automatic priority and ordering

The user must not need to filter out drivers already handled each week.

The default list is automatically partitioned and ordered:

### Priority A — Needs idle attention

Above-threshold drivers whose current-cycle state is:

- `Not Contacted`
- `Attempted`
- `Spoke — Follow-up`

Within this group:

1. open follow-up work first
2. then drivers not yet reached
3. sort by the larger of weighted 28-day and weighted 7-day percentages, descending
4. use Driver Name as a stable final tie-breaker

### Priority B — Above threshold, completed

Above-threshold drivers with current-cycle state `Spoke`.

These remain above the below-threshold fleet so the user can still see that they are high-idle drivers, but they sit below unfinished Priority A work.

### Priority C — Remaining fleet

All other current drivers, with unresolved ordinary work surfaced before fully clear drivers.

Search and optional manual column sorting may temporarily alter presentation, but clearing search/sort returns to this automatic priority order. No weekly reset, saved filter, or “hide contacted” ritual is required.

## 8. Main-list presentation

Each compact, virtualized row shows:

- attention/status text
- Driver Code
- Driver Name
- Unit Code
- Driver Leader
- weighted 28-day idle
- weighted 7-day idle
- current-cycle idle conversation state
- unresolved ordinary-work marker when applicable

Above-threshold values use restrained semantic emphasis: bold text and a subtle warning treatment paired with explicit status wording. Do not rely on color alone.

A compact header line shows:

- current report cycle
- configured threshold
- fleet weighted 28-day idle plus coverage
- fleet weighted 7-day idle plus coverage
- count needing idle attention
- count spoken to this cycle
- `Update Reports` action and last update result

These are compact operational summaries, not giant dashboard cards.

## 9. Driver-card interaction

The selected driver card shows the current percentages, coverage, threshold, current-cycle state, and most recent prior-cycle conversation.

Idle actions are direct:

- `Spoke`
- `Attempted`
- `Spoke — Follow-up`

An optional note field is available without forcing extra forms. Saving an outcome must:

1. persist the event transactionally
2. update the row immediately
3. move the row to its correct priority position
4. keep or advance selection predictably to the next Priority A driver
5. create the appropriate work/handoff record without requiring duplicate typing

Undo/correction should append a corrective event or explicitly replace the current-cycle outcome with an audited change; it must not silently destroy history.

## 10. Report update and rollover behavior

### Application launch

- display the last known-good roster immediately
- run one Downloads scan/import off the UI thread
- apply any accepted report and refresh calculated snapshots
- stop all automatic report activity after that launch update completes

### Manual update

`Update Reports` performs the same complete scan, validation, hash, import, and calculation process. It is the only mid-session report refresh path.

### Same-cycle update

When the accepted report has the same `ReportCycleDate`:

- refresh roster assignments and weighted values
- preserve all current-cycle conversation events
- immediately add newly above-threshold drivers to Priority A
- retain `Spoke` state for drivers already contacted
- move a driver out of above-threshold priority if corrected values no longer exceed the current threshold, while preserving the conversation history

### New-cycle update

When `ReportCycleDate` advances:

- prior events remain historical
- no reset/delete operation runs
- current-cycle state derives as `Not Contacted` for drivers without a new-cycle event
- above-threshold drivers automatically enter Priority A
- below-threshold drivers require no idle conversation unless the user records one voluntarily

## 11. Handoff integration

Idle activity is part of the shift record.

- `Attempted` and `Spoke — Follow-up` remain eligible for unresolved handoff sections
- `Spoke` can appear in completed-today history without cluttering unresolved handoff
- handoff lines include concise driver context and the metric snapshot when useful
- the user never has to retype the idle conversation as a separate work note

## 12. Acceptance criteria

- weighted 28-day calculations use summed hours, not averaged percentages
- four expected weekly observations are required for a complete driver 28-day value
- fleet calculations expose coverage
- a threshold change re-ranks the list immediately
- either 7-day or complete 28-day idle above threshold puts a driver in the above-threshold population
- uncontacted/attempted/follow-up drivers automatically rank above completed conversations
- marking `Spoke` immediately updates state and ordering
- a same-cycle corrected report does not erase contact state
- a newer cycle automatically creates fresh pending state without deleting prior history
- no folder watcher or periodic report polling runs
- the user can work the weekly idle list without applying or maintaining a filter
