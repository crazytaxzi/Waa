# WAA Idle Workflow Specification

## Purpose and cycle key

The fleet queue must show current weighted idle context, whether the current-cycle idle conversation is finished, ordinary unresolved work, and who needs attention next.

The accepted report’s maximum normalized `Week Start Date` is the stable `ReportCycleDate`. Idle contact state is keyed by Driver Code + Report Cycle Date.

- A corrected report with the same cycle preserves contact state.
- A newer cycle derives fresh `Not Contacted` state without deleting prior events.
- Unit or leader changes never split driver identity or rewrite snapshots.

## Weighted calculations

### Driver 7-day

`Idle7d = IdleHours7d / EngineHours7d × 100`

A zero denominator displays `N/A`.

### Driver 28-day

Use the current period and the exact periods 7, 14, and 21 days earlier:

`Idle28d = Sum(IdleHours) / Sum(EngineHours) × 100`

Never average weekly percentages. All four expected observations are required for a complete value. Missing periods display incomplete coverage such as `3/4`; a zero total denominator displays `N/A`.

### Fleet values

Fleet 7-day and 28-day values are also summed numerator/denominator calculations and expose included-driver coverage. Fleet 28-day includes current-roster drivers with complete four-period coverage.

## Threshold

- default `50.0%`
- valid range `0.0` through `100.0`
- strict greater-than comparison
- one threshold applies to valid 7-day and complete 28-day values
- either value above threshold puts the driver in the high-idle population
- changing the threshold reranks immediately and never changes saved contact/work history

## Contact outcomes

- `Not Contacted` — no event for the current cycle
- `Attempted` — driver not reached; actionable
- `Spoke` — current-cycle idle conversation complete
- `Spoke — Follow-up` — driver reached, further action remains open

Each event snapshots:

- Driver Code and report cycle
- UTC timestamp
- outcome and optional note
- weighted 7-day percentage
- weighted 28-day percentage or incomplete coverage
- threshold
- Unit Code
- Driver Leader
- source import ID

## Automatic idle-to-work linkage

A new idle event and its linked work entry are one atomic SQLite transaction. Either both save or neither saves.

Mappings:

- `Spoke` → `Done`, resolved at the event timestamp
- `Attempted` → `FollowUp`, unresolved
- `Spoke — Follow-up` → `FollowUp`, unresolved

Generated text uses the event’s metric snapshots, not later recalculation. Incomplete 28-day coverage is stated explicitly. An optional note is appended concisely.

A partial unique index permits at most one work entry per linked idle event. Initialization idempotently backfills older idle events that predate the work-log feature.

## Queue ordering

The queue uses these required bands:

1. **Above threshold, idle unfinished** — `Not Contacted`, `Attempted`, or `Spoke — Follow-up`.
2. **Above threshold, Spoke** — drivers with ordinary unresolved work first within this band.
3. **Remaining drivers with unresolved ordinary work.**
4. **Remaining clear drivers.**

Within band 1:

1. `Spoke — Follow-up`
2. `Attempted`
3. `Not Contacted`
4. highest current idle concern, using the larger valid 7-day/complete 28-day value
5. Driver Name and Driver Code as stable tie-breakers

The queue obtains unresolved counts with one indexed aggregate query, not one history query per row.

## Driver-card behavior

The selected-driver pane presents:

1. identity and idle context
2. current-cycle idle-contact actions
3. Open Work
4. New Work
5. Today’s Activity
6. Next Needing Attention

Saving an idle outcome:

1. inserts the event and linked work atomically
2. refreshes the row status, open count, selected-driver activity, and queue order
3. may advance predictably to the next visible driver needing attention

The user never retypes the same idle conversation as ordinary work.

## Next Needing Attention

The direct action examines only the current visible/search-filtered queue.

- Prefer another driver with unfinished high-idle contact work.
- Otherwise choose another driver with unresolved ordinary work.
- Do not jump to a driver hidden by search.
- If no other visible driver needs attention, retain selection and report that clearly.

## Report update behavior

- show saved data first
- scan/import once during launch
- thereafter update only through `Update Reports`
- no `FileSystemWatcher`, recurring scan, or polling timer
- same-cycle updates refresh assignments/metrics while preserving contacts
- new-cycle updates preserve history and naturally derive new pending state
- rejected reports retain the last known-good roster, contacts, work, settings, and appearance

## Handoff behavior

Idle activity is rendered only through its linked work entry:

- unresolved Attempted and Spoke — Follow-up entries appear in `NEEDS FOLLOW-UP`
- current-day Spoke entries appear in `COMPLETED TODAY`
- no event is rendered a second time from `idle_contact_events`
- metric and assignment context come from the historical snapshots

## Acceptance criteria

- weighted 28-day calculations use summed raw hours and require four expected observations
- threshold changes immediately rerank without rewriting history
- unfinished high-idle work ranks before completed high-idle and ordinary work
- idle event + work insertion is atomic
- legacy event backfill is repeatable without duplication
- marking Spoke updates state/order immediately
- same-cycle correction preserves contact state
- new cycle creates fresh pending state without deleting history
- Next respects search and prefers idle attention
- no report watcher or periodic polling exists
