# WAA Project Plan

## 1. Product goal

WAA is a personal work-through and handoff tool for driver support work. Its job is not to look impressive. Its job is to make the shift easier to work, reduce missed follow-up, and make the end-of-day handoff accurate.

The driver is the center of the working model. Reports can attach information or work to a driver, unit, trailer, or leader, but those objects remain separate concepts.

## 2. Product rules

1. **Driver-centric, not truck-centric.** Driver Code is the durable key. Unit assignment is observed context.
2. **The work list is the home screen.** No dashboard must be cleared before useful work can begin.
3. **Common actions stay shallow.** Selecting a driver and recording work should not require modal chains or navigation through multiple pages.
4. **Preserve the last known-good state.** Bad, incomplete, or locked report files must not blank the roster.
5. **Never silently guess identity.** Ambiguous driver labels or conflicting same-period unit assignments must be surfaced rather than auto-merged incorrectly.
6. **Status uses words first, color second.** Color communicates state or urgency, never decoration.
7. **No feature gets in merely because data exists.** A report field must improve the work-through or handoff flow before it is shown.
8. **No old-repository archaeology.** Previous WAA history is not a source for architecture, schemas, workflow, or styling unless the user explicitly requests otherwise.

## 3. Primary source and roster behavior

The initial roster source is the newest valid file matching:

`rolling 7 day_data*.csv`

in the current user's Windows Downloads known folder.

### Startup

On application startup:

1. Resolve the real Downloads known folder rather than assuming a literal `%USERPROFILE%\\Downloads` path.
2. Scan matching Rolling 7 Day CSV files.
3. Sort candidates by last-write time, newest first.
4. Validate required headers before accepting a candidate.
5. Confirm the file can be read and is no longer actively changing.
6. Hash its contents.
7. Skip re-import if the hash was already successfully imported.
8. Parse and commit the roster in one transaction.
9. If the newest candidate is invalid, keep the last known-good roster and expose a compact sync warning.

### Live refresh

Use `FileSystemWatcher` for Created, Changed, and Renamed notifications, but do **not** treat one event as one import. Windows file operations can raise duplicate events.

Watcher behavior:

- filter to CSV activity in Downloads
- keep event handlers very short
- debounce bursts of notifications
- after the debounce, rescan the matching candidate set
- select the newest valid stable file
- import only when its content hash is new
- perform a full rescan after watcher errors or buffer overflow

The watcher is therefore a wake-up signal; the directory scan is the source of truth.

## 4. Rolling 7 Day normalization

The supplied representation contains two rows for the same driver/week because `Measure Names` contains `OOR %` and `Idle %`. Operational metadata such as driver, unit, leader, terminal, and cost center repeats across those rows.

For the roster:

- use the newest available `Week Start Date` for each driver
- deduplicate measure rows before creating current roster entries
- keep `Unit Code` as text, not an integer
- keep the complete raw driver label for audit/debugging
- extract Driver Code and Driver Name only through a tested parser for the real export format
- preserve older weekly unit observations instead of overwriting history
- if one driver has multiple distinct Unit Codes in the same newest period, mark the assignment ambiguous until the rule is understood
- if a driver disappears from the newest report, do not delete the driver; mark the roster observation as no longer current

### Required initial fields

Driver:

- `DriverCode` — primary key
- `DriverName`
- `CurrentDriverLeader`
- `CurrentUnitCode`
- `DriverTerminal`
- `FleetLeader`
- `CostCenter`
- `OpsLob`
- `RosterWeekStart`
- `LastSeenUtc`

Unit:

- `UnitCode` — primary key, stored as text

Observation:

- `DriverCode`
- `UnitCode`
- `DriverLeader`
- `WeekStart`
- `SourceImportId`

Import:

- source file name/path
- file last-write timestamp
- SHA-256
- import timestamp
- accepted/rejected status
- validation/error detail when rejected

## 5. Work model

The first useful work model should remain intentionally small.

A driver may have multiple shift entries. Each entry contains:

- automatic timestamp
- Driver Code
- short work text
- status: `Done`, `Waiting`, or `Follow-up`
- optional resolution timestamp

Do not require category selection for every note in the first build. Categories can be added later only if real use proves they save time.

### Carry-forward

Unresolved `Waiting` and `Follow-up` entries remain visible on the driver's work card on later shifts until resolved. They should not need to be copied manually into a new day's notes.

## 6. Main-screen UX

The initial application should use one main window.

### Left: driver list

Each compact row shows:

- Driver Code
- Driver Name
- current Unit Code
- Driver Leader
- a small text status only when unresolved work exists

Controls:

- search by driver code, driver name, or unit
- optional Driver Leader filter
- refresh/sync state in a quiet status area

Do not put weekly percentages, charts, coaching scores, report counts, or large metrics in this list.

### Center: selected driver work card

Header:

- Driver Name
- Driver Code
- Unit Code
- Driver Leader

Body:

- unresolved carry-forward first
- today's entries in chronological order
- one obvious text box for a new work entry
- three direct save actions: Done, Waiting, Follow-up

The entry box should receive focus quickly after a driver is selected. Saving should append immediately and keep the user in the flow.

### Handoff view

Handoff is the only secondary top-level view needed initially.

Generate editable text grouped as:

1. Needs Follow-up
2. Waiting / Pending
3. Completed Today
4. General notes only if a later requirement introduces them

Each line should identify the driver by useful operational context, for example:

`270139 — DRIVER NAME (CODE): waiting on ...`

The handoff is editable before copying. Generated text should never modify the underlying work entries merely because the user edits the handoff draft.

## 7. Visual design

Use a restrained Windows workplace appearance inspired by Fluent principles, not a branded imitation.

- light neutral surfaces
- standard system typography
- modest borders and spacing
- no continuous animation
- no backdrop blur requirement
- no glow
- no decorative gradients
- no giant cards
- no charting in the initial product
- semantic color only for meaningful state, always paired with text

The application should look acceptable if a supervisor walks past the monitor and glances at it for two seconds.

## 8. Technical architecture

### Runtime

- .NET 10 LTS
- WPF
- Windows x64 initial target
- self-contained publish
- prefer single-file publish if all required dependencies remain reliable in that mode

### Persistence

Use SQLite through `Microsoft.Data.Sqlite`.

Store the database under the current user's local application data, for example:

`%LOCALAPPDATA%\\WAA\\waa.db`

Use transactions for each import and for logically related mutations. WAL may be enabled for responsive reads while small writes occur, but the app should keep write transactions short.

No Redis, LMDB, web server, browser client, background cloud service, or distributed architecture is justified for this product.

### Process shape

One desktop process owns:

- WPF UI
- report discovery/import
- file watcher
- SQLite access
- handoff generation

Keep the architecture separable in code without creating separate processes:

- `Domain` — Driver, Unit, WorkEntry, Handoff models/rules
- `Data` — SQLite schema/repositories/migrations
- `Imports` — Downloads discovery, Rolling 7 Day parser, validation, hashing
- `UI` — WPF views/view-models

## 9. Failure behavior

The app must fail quietly and usefully.

- Partial/locked CSV: retry only after the file settles; retain last good roster.
- Missing required column: reject the candidate and say which column is missing.
- Unknown driver label format: reject identity parsing for that row; do not invent a Driver Code.
- Duplicate watcher events: collapse through debounce/hash.
- SQLite write failure: do not report a successful save; keep the typed entry visible so it can be retried.
- Corrupt database: stop writes and surface a clear recovery message; do not silently recreate and lose history.

## 10. Development phases

### Phase 1 — Roster foundation

Build and verify only:

- .NET 10 WPF shell
- SQLite initialization
- Downloads known-folder resolution
- Rolling 7 Day candidate discovery
- validation + stable-file read + SHA-256
- parser and deduplication
- Driver / Unit / observation persistence
- searchable driver list
- automatic refresh after a new download
- small visible sync state

**Exit criteria**

- the same driver remains the same entity when Unit Code changes
- duplicated OOR/Idle rows do not create duplicate drivers
- newest valid report becomes current without restarting the app
- duplicate file-system events do not duplicate imports
- a malformed newest file cannot wipe the last good roster
- conflicting same-period unit observations are not silently guessed

### Phase 2 — Driver work-through

Add:

- driver work card
- timestamped work entry
- Done / Waiting / Follow-up
- unresolved carry-forward
- fast next-driver/search behavior
- useful keyboard focus and shortcuts after mouse flow is correct

**Exit criteria**

- a normal driver update can be recorded without leaving the main screen
- saved work survives restart
- unresolved work is immediately visible when that driver is reopened

### Phase 3 — Handoff

Add:

- handoff generation from real work entries
- editable handoff draft
- Copy to Clipboard
- unresolved-first ordering
- completed-today section

**Exit criteria**

- the user can produce the end-of-shift handoff without manually rereading every driver
- editing the draft does not mutate historical work entries

### Phase 4 — Missing BOL integration

Only after Phases 1–3 are proven comfortable:

- import newest Missing BOL report
- attach items through Last Dispatch Driver Code
- expose them as driver work context, not driver identity
- allow handling/resolution state without destroying source evidence

### Phase 5 — Maintenance and DOT evaluation

Evaluate separately rather than forcing them into the driver card.

- maintenance report is asset/Driver-Leader oriented
- DOT report is trailer oriented

Add them only if they clearly reduce work or missed follow-up. They may become separate compact queues rather than driver children.

## 11. Research basis

Fresh research used for this plan:

- Microsoft .NET support policy — .NET 10 is LTS and supported through November 2028: https://dotnet.microsoft.com/en-us/platform/support/policy
- Microsoft WPF desktop guidance and .NET 10 WPF updates: https://learn.microsoft.com/en-us/dotnet/desktop/
- .NET single-file/self-contained publishing: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Windows known-folder guidance: https://learn.microsoft.com/en-us/windows/win32/shell/known-folders
- `FileSystemWatcher` behavior and duplicate events: https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher
- `Microsoft.Data.Sqlite` transactions/WAL guidance: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions and https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
- Fluent 2 color/layout principles: https://fluent2.microsoft.design/color and https://fluent2.microsoft.design/layout
- GOV.UK task-list pattern for clear task names and explicit status: https://design-system.service.gov.uk/components/task-list/
