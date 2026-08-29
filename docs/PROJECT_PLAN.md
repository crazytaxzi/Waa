# WAA Project Plan

## 1. Product goal

WAA is a personal driver-support work-through and handoff tool. Its job is to make the shift easier to work, prevent missed follow-up, show fleet idle performance clearly, and produce an accurate end-of-day handoff.

The driver is the center of the working model. Reports may attach information or work to a driver, unit, trailer, or leader, but those objects remain separate concepts.

The application must look calm, clean, and unmistakably work-related. It is designed for a low-spec Windows PC and must not spend resources on decoration, browser infrastructure, continuous report monitoring, or background activity that does not directly help the user.

## 2. Product rules

1. **Driver-centric, not truck-centric.** Driver Code is the durable key. Unit assignment is observed context.
2. **The work list is the home screen.** No dashboard must be cleared before useful work can begin.
3. **Idle visibility belongs in the fleet list.** Weighted 28-day and 7-day percentages must be visible without opening every driver.
4. **The list manages weekly attention automatically.** Drivers needing an idle conversation rise to the top; completed conversations move below unfinished high-idle work without manual filtering.
5. **Common actions stay shallow.** Selecting a driver, recording work, and recording an idle outcome must not require modal chains or multiple pages.
6. **Report updates are controlled.** Import once at launch and thereafter only through `Update Reports`; no watcher or polling loop.
7. **Preserve the last known-good state.** Bad, incomplete, or locked report files must not blank the roster.
8. **Never silently guess identity or data conflicts.** Ambiguous driver labels and conflicting same-period observations must be surfaced.
9. **Status uses words first, color second.** Color communicates state or urgency, never decoration.
10. **No feature gets in merely because data exists.** A report field must improve work-through or handoff before it is shown.
11. **No old-repository archaeology.** Previous WAA history is not a source for architecture, schema, workflow, or styling unless the user explicitly requests a specific historical item.

## 3. Low-end Windows constraints

The first release is a native Windows desktop utility intended to remain responsive on modest office hardware.

### Required design choices

- native WPF window and controls
- no WebView, browser client, local HTTP server, Node runtime, or cloud dependency
- no continuous animation, blur, glow, animated charts, or decorative GPU work
- no report watcher, periodic folder scan, or polling timer
- virtualized fleet rows so only visible rows are realized
- indexed database queries for default priority ordering and search
- calculate and persist idle snapshots during import rather than recalculating history while scrolling
- load the last known-good roster immediately, then run the one launch update off the UI thread
- keep database write transactions short
- avoid large image assets and unnecessary third-party UI frameworks

### Performance acceptance

- cached roster data becomes usable without waiting for Downloads scanning
- launch update and manual update do not freeze typing, scrolling, or window movement
- after launch update completes, the idle application performs no recurring report work
- search, selection, and status changes feel immediate across the expected fleet size
- ordinary list scrolling does not trigger per-row historical queries

Performance must be validated on a representative low-end Windows machine rather than inferred only from a development PC.

## 4. Primary source and update behavior

The initial roster and idle source is the newest valid file matching:

`rolling 7 day_data*.csv`

in the current user's Windows Downloads known folder.

### Application launch update

1. Open the local database and show the last known-good roster immediately.
2. Resolve the actual Windows Downloads known folder rather than assuming a literal `%USERPROFILE%\\Downloads` path.
3. Scan matching Rolling 7 Day CSV files once.
4. Rank candidates by report-cycle date when readable, with last-write time as a discovery/tie-break signal.
5. Validate required headers and source shape.
6. Confirm the chosen file can be read consistently and is not locked/partially written.
7. Hash the content.
8. Skip import when that exact hash was already accepted.
9. Parse, normalize, calculate, and commit in one transaction.
10. Refresh the fleet list and stop automatic report activity.

### Manual update

A quiet, visible `Update Reports` command runs the exact same full scan and validation path. This is the only way to import a newly downloaded or corrected report while WAA remains open.

There is no `FileSystemWatcher`, no recurring directory scan, and no import triggered merely because a file appeared in Downloads.

### Update result

The header status area reports one concise outcome:

- updated from file name and report cycle
- already current
- no matching report found
- newest candidate rejected, with the specific reason

A rejected update retains the last known-good roster and all local work history.

## 5. Rolling 7 Day normalization

The supplied representation contains two rows for the same driver/week because `Measure Names` contains `OOR %` and `Idle %`. Driver, unit, leader, engine hours, and idle hours repeat across those measure rows.

WAA normalizes them into one weekly observation per Driver Code and report-cycle date.

### Identity and context

- keep `Unit Code` as text, not an integer
- keep the complete raw driver label for audit/debugging
- extract Driver Code and Driver Name only through a tested parser for the real unredacted export format
- preserve older weekly unit/leader observations instead of overwriting history
- if one driver has conflicting values within the same period, mark the source conflict rather than choosing silently
- if a driver disappears from the newest report, preserve the driver and work history but mark the roster observation as no longer current

### Required fields

Driver:

- `DriverCode` — primary key
- `DriverName`
- `CurrentDriverLeader`
- `CurrentUnitCode`
- `DriverTerminal`
- `FleetLeader`
- `CostCenter`
- `OpsLob`
- `CurrentReportCycleDate`
- `LastSeenUtc`
- `IsCurrentRoster`

Weekly idle observation:

- `DriverCode`
- `ReportCycleDate`
- `EngineHours7d`
- `IdleHours7d`
- `UnitCode`
- `DriverLeader`
- `SourceImportId`
- source-quality/conflict state

Unit:

- `UnitCode` — primary key, stored as text

Import:

- source file name/path
- file last-write timestamp
- SHA-256
- report-cycle date
- import timestamp
- accepted/rejected status
- validation/error detail when rejected

## 6. Weighted idle calculations

Percentages are calculated from raw hours at import time and stored at full precision. The UI rounds only for display.

### Driver 7-day

`Idle7d = IdleHours7d / EngineHours7d × 100`

The current value uses the observation for the accepted report's maximum normalized `Week Start Date`.

### Driver 28-day

Use the current report period and the three expected periods exactly 7, 14, and 21 days earlier:

`Idle28d = Sum(IdleHours across 4 expected periods) / Sum(EngineHours across those periods) × 100`

Do not average weekly percentages. All four expected period rows are required for complete 28-day coverage. Missing periods show `Incomplete` and coverage such as `3/4`. A zero total engine denominator shows `N/A`.

### Fleet calculations

- Fleet 7-day = sum of current valid idle hours / sum of current valid engine hours.
- Fleet 28-day = sum of four-period idle hours / sum of four-period engine hours for current-roster drivers with complete coverage.
- Both fleet summaries expose included-driver coverage.

### Threshold

- default `50.0%`
- locally configurable from `0.0` through `100.0`
- strict greater-than comparison
- initially shared by both 7-day and 28-day values
- reranks the list immediately when changed
- does not rewrite historical conversation records

A driver is above threshold when either the valid current 7-day value or complete current 28-day value exceeds the threshold.

## 7. Idle conversation accountability

Idle conversation state is tied to Driver Code and current report-cycle date. It is not a disposable weekly checkbox.

### Outcomes

- `Not Contacted`
- `Attempted`
- `Spoke`
- `Spoke — Follow-up`

`Attempted` remains incomplete. `Spoke — Follow-up` proves the conversation occurred but remains actionable.

Each event stores the timestamp, optional note, report cycle, weighted percentage snapshots, threshold snapshot, Unit Code, Driver Leader, and source import ID. A later report cannot rewrite what was discussed.

### Automatic rollover

- a corrected report with the same cycle preserves all conversation state
- a later report cycle naturally derives fresh `Not Contacted` state for drivers without a new-cycle event
- prior conversation history remains intact
- there is no reset button, bulk uncheck, or filter-maintenance ritual

### Default ordering

The list automatically orders:

1. above-threshold drivers with `Not Contacted`, `Attempted`, or `Spoke — Follow-up`
2. above-threshold drivers with `Spoke`
3. all remaining current drivers, with unresolved ordinary work before clear drivers

Within unfinished high-idle work, follow-up items come first and then the greatest current idle concern sorts first. The concern value is the larger valid value of weighted 28-day and weighted 7-day idle.

Marking `Spoke` immediately updates the row and moves it below unfinished high-idle drivers. The next unfinished driver can be selected automatically or with one direct action.

See `docs/IDLE_WORKFLOW.md` for the authoritative rules.

## 8. Work model

A driver may have multiple shift entries. Each entry contains:

- automatic timestamp
- Driver Code
- short work text
- status: `Done`, `Waiting`, or `Follow-up`
- optional resolution timestamp
- optional linked idle-conversation event

Do not require a category for every ordinary note. Idle conversations are structured because weekly accountability depends on knowing whether the driver was actually reached.

### Carry-forward

Unresolved `Waiting` and `Follow-up` entries remain visible on later shifts until resolved. They do not need to be copied into a new day's notes.

An idle action should create the appropriate work/handoff record automatically so the user does not type the same conversation twice.

## 9. Main-screen UX

The initial application uses one main window.

### Compact fleet header

Show in one restrained line or compact toolbar:

- current report cycle
- configurable threshold
- fleet weighted 28-day percentage and coverage
- fleet weighted 7-day percentage and coverage
- count needing idle attention
- count spoken to this cycle
- `Update Reports`
- last update result

These are operational summaries, not large KPI tiles.

### Fleet list

Each virtualized row shows:

- attention/status wording
- Driver Code
- Driver Name
- current Unit Code
- Driver Leader
- weighted 28-day idle percentage or coverage state
- weighted 7-day idle percentage
- current-cycle idle conversation status
- unresolved ordinary-work indicator when present

Controls:

- search by driver code, name, or unit
- optional Driver Leader filter
- sortable columns when needed
- one action to restore the automatic priority order

Above-threshold values receive restrained semantic emphasis paired with text. Do not rely on color alone.

### Selected driver work card

Header:

- Driver Name
- Driver Code
- Unit Code
- Driver Leader
- current 28-day and 7-day idle context
- current cycle conversation status
- most recent prior-cycle conversation date/outcome

Body:

- unresolved carry-forward first
- today's entries in chronological order
- one obvious text box for ordinary work
- direct Done, Waiting, and Follow-up actions
- direct `Spoke`, `Attempted`, and `Spoke — Follow-up` idle actions with an optional note

Saving must append immediately, update ordering, and keep the user in the work-through flow.

### Handoff view

Handoff remains the only secondary top-level view initially.

Generate editable text grouped as:

1. Needs Follow-up
2. Waiting / Pending
3. Completed Today

Idle `Attempted` and `Spoke — Follow-up` events remain eligible for unresolved sections. Completed `Spoke` events may appear in completed-today history without flooding the unresolved handoff.

Generated text is editable before copying. Editing the draft never changes underlying work or conversation records.

## 10. Visual design

Use a restrained Windows workplace appearance inspired by Fluent principles, not a branded imitation.

- light neutral surfaces
- standard system typography
- compact aligned percentage columns
- modest borders and spacing
- no continuous animation
- no backdrop blur requirement
- no glow
- no decorative gradients
- no giant cards
- no decorative charts
- semantic color only for meaningful state and always paired with wording/iconography

The application should look acceptable if a supervisor walks past and glances at it for two seconds.

## 11. Technical architecture

### Runtime

- .NET 10 LTS
- WPF
- Windows x64 initial target
- self-contained publish
- prefer single-file publish only if startup and dependency behavior remain reliable

### Persistence

Use SQLite through `Microsoft.Data.Sqlite` under the current user's local application data, for example:

`%LOCALAPPDATA%\\WAA\\waa.db`

Use transactions for complete imports and logically related mutations. Keep writes short. WAL may be enabled if it improves measured responsiveness, but it is not an excuse for unnecessary concurrency.

No Redis, LMDB, web server, browser client, background cloud service, or distributed architecture is justified.

### Process shape

One desktop process owns:

- WPF UI
- launch/manual report discovery and import
- weighted snapshot calculation
- SQLite access
- work/conversation recording
- handoff generation

Code boundaries:

- `Domain` — Driver, Unit, idle calculation, priority, work, conversation, handoff rules
- `Data` — SQLite schema, repositories, migrations, indexed queries
- `Imports` — Downloads discovery, CSV parser, validation, hashing, normalization
- `UI` — WPF views/view-models and virtualized presentation

The application must not create separate processes merely to appear architecturally sophisticated.

## 12. Failure behavior

- partial/locked CSV: reject the update and retain last good data
- missing required column: identify the actual missing header
- unknown driver label format: reject identity parsing for that row; never invent a code
- conflicting duplicated measure rows: record a data-quality error; never choose silently
- missing 28-day period: show incomplete coverage instead of a normal percentage
- zero denominator: show `N/A`
- SQLite write failure: do not claim success; preserve the typed entry for retry
- corrupt database: stop writes and provide a clear recovery path; never silently recreate and lose history
- manual update clicked twice: prevent concurrent imports and report that an update is already running

## 13. Development phases

### Phase 1 — Roster and weighted idle foundation

Build and verify:

- .NET 10 WPF shell
- SQLite initialization/migrations
- Downloads known-folder resolution
- one automatic launch update
- explicit `Update Reports` action
- Rolling 7 Day candidate discovery and validation
- stable read, SHA-256, and atomic import
- repeated-measure normalization
- Driver / Unit / weekly observation persistence
- weighted driver 7-day and complete-coverage 28-day calculation
- weighted fleet summaries with coverage
- configurable threshold
- searchable, virtualized, automatically prioritized fleet list
- compact update status

**Exit criteria**

- same driver remains the same entity when Unit Code changes
- duplicated OOR/Idle rows do not duplicate drivers or weekly observations
- 28-day results use summed hours and require four expected periods
- fleet percentages expose coverage
- cached roster is usable before launch import completes
- no watcher or recurring scan runs
- a newly downloaded report is ignored until launch or `Update Reports`
- duplicate/manual re-import of the same hash does not duplicate observations
- malformed newest data cannot wipe the last good roster
- threshold changes immediately rerank the list

### Phase 2 — Idle conversation workflow

Add:

- per-driver/current-cycle state
- `Spoke`, `Attempted`, and `Spoke — Follow-up`
- immutable context snapshots
- automatic weekly rollover by report-cycle date
- same-cycle correction preservation
- unfinished-first priority ordering
- fast next-needing-attention behavior
- automatic work/handoff linkage

**Exit criteria**

- the user never filters already-contacted drivers out manually
- marking `Spoke` immediately changes state and ordering
- a same-cycle corrected report preserves conversation state
- a new report cycle creates fresh pending state without deleting history
- attempted and follow-up outcomes remain actionable

### Phase 3 — General driver work-through

Add:

- driver work card
- timestamped Done / Waiting / Follow-up entries
- unresolved carry-forward
- useful keyboard focus and shortcuts after the mouse flow is correct

**Exit criteria**

- a normal driver update can be recorded without leaving the main screen
- saved work survives restart
- unresolved work is immediately visible when the driver is reopened

### Phase 4 — Handoff

Add:

- handoff generation from work and idle events
- editable handoff draft
- Copy to Clipboard
- unresolved-first ordering
- completed-today section

**Exit criteria**

- end-of-shift handoff requires no manual rereading of every driver
- editing the draft does not mutate historical records

### Phase 5 — Missing BOL integration

Only after the core workflow is proven comfortable:

- import newest Missing BOL report on launch/manual update
- attach items through Last Dispatch Driver Code
- expose them as driver work context, not identity
- allow handling/resolution state without destroying source evidence

### Phase 6 — Maintenance and DOT evaluation

Evaluate separately rather than forcing them into every driver card.

- maintenance is asset/Driver-Leader oriented
- DOT is trailer oriented

Add them only if they clearly reduce work or missed follow-up. They may become separate compact queues.

## 14. Research basis

Fresh research already used for the technical direction:

- Microsoft .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- Microsoft WPF desktop guidance: https://learn.microsoft.com/en-us/dotnet/desktop/
- .NET single-file/self-contained publishing: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Windows known-folder guidance: https://learn.microsoft.com/en-us/windows/win32/shell/known-folders
- `Microsoft.Data.Sqlite` transactions/WAL guidance: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions and https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
- WPF UI virtualization guidance: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls
- Fluent color/layout principles: https://fluent2.microsoft.design/color and https://fluent2.microsoft.design/layout
- GOV.UK task-list pattern: https://design-system.service.gov.uk/components/task-list/
