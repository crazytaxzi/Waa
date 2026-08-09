# WAA — Complete Codex Build Mission

Build this repository from its current state into the finished WAA application in one uninterrupted run. Do not stop at planning, scaffolding, MVP, phases, placeholders, TODOs, mock-only controls, or partial pages. Inspect the repo first, preserve correct existing idle logic, then implement, test, debug, polish, commit, and push the complete working result to `main`.

## Non-negotiable runtime constraints

Target is a locked-down Windows company computer. The finished app must require no installation, administrator rights, Python, Node/npm, Java, Electron, package manager, PowerShell Gallery module, CDN, Internet connection, cloud service, or browser extension.

Use only:
- Windows PowerShell and built-in Windows/.NET functionality
- HTML/CSS/vanilla JavaScript/ES modules/SVG
- a portable official `sqlite3.exe` bundled in the repo under `runtime/sqlite/`

Launch with `Start-Waa.ps1` (optionally `Start-Waa.cmd`). Start a local HTTP server bound strictly to `127.0.0.1`, preferably with `System.Net.Sockets.TcpListener`, choose port 8765 or a small fallback range, and open the default browser automatically. Never bind to `0.0.0.0` or expose the app to the LAN.

## Database

Use SQLite as the only persistent operational database. Runtime DB location: `%LOCALAPPDATA%\Waa\waa.db`. Do not persist the app database in JSON, JSONL, XML, IndexedDB, or localStorage. JSON is allowed only as transient localhost API transport.

Centralize all SQLite access in one PowerShell database layer. Use migrations, foreign keys, indexes, transactions, WAL, busy timeout, integrity checks, and safe SQLite backups. Every import is transactional: validation/insertion failure must roll back the whole import. Store the exact raw imported source in SQLite as evidence, with SHA-256 duplicate detection.

The driver is the persistent entity. Truck number is only a time-dependent assignment/observation. Never permanently identify a driver by truck.

Core data must support canonical drivers, aliases, truck history, PTA observations, idle periods, Missing BOLs, driver work items, notes, reminders, timers, transition drafts, safety notes, import batches, settings, and audit history.

Unknown data stays `Unknown`; never invent semantics.

## Driver identity

PTA names use an 8-character code approximately `LAST NAME + FIRST INITIAL` with surname truncated to fit. Required examples:
- Orlando Carmona -> `CARMONAO`
- Bruce D Ratcliff -> `RATCLIFB`
- Patrick Lachica Encinas -> `LACHICAP`
- Joan M Hernandez Lopez -> `HERNANDJ`
- Guadalupe Ochoa Felix -> `OCHOAFEG`
- Clarence Broadbrooks -> `BROADBRC`

Normalize surname components, take up to seven surname characters, append first-name initial. Retain company dispatch driver codes and full names as independent aliases. Never auto-merge ambiguous collisions. Provide a Data Quality UI to resolve unmatched/ambiguous drivers manually and persist confirmed aliases.

## Import system

One Import/Data Quality page must accept pasted text and files. Flow: Detect -> Parse -> Validate -> Preview -> Confirm -> Transactional Import -> Refresh. Preview must never modify the database. On commit, reparse/revalidate server-side rather than trusting browser preview data.

Each import records raw source, hash, timestamp, type, parser version, filename/source type, row counts, warnings/errors, and normalized records.

### PTA / fleet-state paste

Parse the previously supplied 11-column format:
1. Truck
2. Division
3. Driver Code
4. PTA
5. Operational Status
6. Planning Status
7. Operational Note
8. Driver Type
9. Location
10. Source Numeric 1
11. Source Numeric 2

The last two numeric fields are intentionally unknown; preserve them as `source_numeric_1` and `source_numeric_2` without inventing meanings.

Support Markdown pipe tables, tabs, extra whitespace, separator rows, blank cells/drivers, alphanumeric divisions, escaped values such as `Clean\_QA`, Solo, Team, Mentor/Stu., and future unknown codes. Every import is a historical snapshot.

PTA priority: any legitimate driver PTA whose clock time is exactly `23:57` is pinned above every other actionable PTA. Then sort chronologically ascending: oldest overdue -> newest overdue -> near future -> far future.

Equipment/sentinel rows such as blank driver + `12/31/26 23:59` with statuses like Shop, TruckPrep, Reserved, ClaimsHold, Clean_QA, or GoodToGo are not actionable driver PTAs. Preserve source value but effective actionable PTA is N/A. A legitimate assigned-driver 23:59 is not automatically a sentinel.

Manual PTA edits create a new historical/manual PTA observation; never overwrite imported evidence.

### Rolling idle report

Port the existing Python idle behavior into PowerShell so the finished app has no Python dependency. Store base measurements: driver, truck, period start/end, engine hours, idle hours. Percentages are derived.

Fleet idle must be weighted: `SUM(idle_hours) / SUM(engine_hours) * 100`. Never average driver percentages. Zero engine hours means No Data, not 0%. Validate 28-day coverage to avoid double-counting overlapping rolling periods; show Partial Data if complete valid coverage is unavailable.

### Missing BOL report

Support the supplied `Order Details Missing BOL.csv`, which is actually UTF-16 with BOM, tab-delimited, and 29 columns. Detect content rather than trusting extension. Preserve all source fields. Use `Last Dispatch Driver cd` and `Last Dispatch Driver nm` as identity evidence. Match to canonical driver, never merely by truck. Track records historically. Do not assume disappearance from a later report proves resolution until that business rule is known.

## UI design

Create a polished, flashy, readable Gothic cybernetic operations console. Dark gunmetal backgrounds, neon green and neon purple accents, bright safe blue, deep red alerts, near-white text, slightly high contrast. Centralize theme tokens. Use angular/cut corners, subtle cathedral/tracery geometry, neon edge glow, dramatic headers, strong hover/focus states, and restrained motion. No unreadable blackletter body text, Halloween clichés, strobing, or clutter. Use Windows-native fonts and add Reduce Motion.

Suggested palette: `#0B0E12`, `#141920`, `#1B222B`, `#303946`, `#7CFF3A`, `#B34CFF`, `#36BFFF`, `#811827`, `#FF405D`, `#F3F6F8`, `#AAB4C0`.

Use only HTML/CSS/vanilla JS/ES modules/SVG. No frontend framework, third-party chart library, CDN, or build step. Use reusable components and hash routes: Dashboard, PTA Tracking, Workflow, Missing BOLs, Transition, Imports/Data Quality.

## Dashboard

Show:
- interactive neon-green SVG fleet 7-day rolling idle graph for all valid history
- interactive neon-purple SVG fleet 28-day graph
- count of drivers above 50% latest valid 7-day idle
- `Heroes`: five lowest valid idle drivers
- `Heroes in Training`: five highest valid idle drivers

Hero cards show rank, driver, current truck, 7D %, 28D %, engine hours, and trend. Purpose is to learn from low idlers and coach high idlers.

## PTA Tracking

Fast attention queue showing PTA, relative PTA, truck, driver, division, operational status, planning status, note, driver type, and location. Provide strong search/filtering. Use deep red/red for overdue, purple/magenta for immediate attention, bright blue for safely future, always with text labels. Maintain 23:57 priority rule. Clicking driver/truck opens the shared Driver Work Card.

## Workflow + Driver Work Card

Workflow lists current truck/driver assignments and is sortable/searchable/filterable. Build exactly ONE reusable Driver Work Card used throughout the app. Everything actionable for a driver should be accessible from it.

Header: truck, full driver name if known, PTA code, division, status, planning status, driver type, location, current PTA, data freshness.

Idle section: 7D/28D %, engine/idle hours, trend, historical graph.

PTA section: effective PTA, relative time, priority, source/updated time, inline edit.

Missing BOL section: active BOLs with order, date, origin/destination, mileage/type, and persistent `Mentioned to Driver` checkbox/timestamp.

Home Time section: checked, expected to work, OK/Concern/Unknown, reason, actions. Keep manual until a reliable future source exists.

On-Time/Late section: Unknown/On Time/At Risk/Late, reason, actions, last checked.

Preplan section: show source status separately from workflow fields Reviewed + Accepted/Denied/Unknown + note. Never equate `Preplan` with accepted.

Routing section: Checked + Accurate/Needs Correction/Unknown + note + actions.

Safety: SQLite-backed safety-note library, random useful note, New Random Note, Mentioned checkbox, avoid immediate repeats.

Notes: chronological persistent driver notes, not one destructive blob.

Reminders: text, due datetime, completed, snooze; overdue items appear after restart.

Timers: persistent target timestamps; survive restart.

Transition: Include in Transition toggle + Transition Note for current work context. Do not carry stale operational checkmarks blindly into a new load/PTA cycle.

## Missing BOL page

Driver-oriented queue: driver, current truck, order, empty-call date, origin, destination, age, type, mentioned status. Search/filter/sort. Clicking driver/truck opens the same Driver Work Card.

## Transition page

Plain editable text only; no Outlook/email UI. Start exactly:

`No Open ACE/ACI's`

Then selected current work items in truck-number order as:

`<Truck#> - <Driver Name> : <Transition Note>`

Provide Regenerate, editable text area, Copy All, and persistence of manually edited draft. Never regenerate underneath active manual editing.

## Security + backups

Loopback only. Prevent path traversal. Serve static files only from web root. No external CORS. Strict CSP. Never execute imported content or construct commands from imported values. Keep SQL access centralized/escaped/validated.

Provide SQLite-safe automatic backups, pre-migration backups, Backup Now, and Restore UI under `%LOCALAPPDATA%\Waa\backups`. Run `PRAGMA integrity_check` at startup. If integrity fails, stop normal writes and offer restore; never silently delete damaged data.

## Audit + Data Quality

Audit meaningful actions: identity links, PTA edits, BOL mentions, home-time review, on-time status, preplan response, routing review, safety discussion, transition selection, reminders, etc. Expose driver activity/history.

Data Quality must surface unmatched/ambiguous identities, malformed rows, invalid dates, duplicate imports, unknown source codes, parser warnings, and incomplete idle coverage.

## Testing

Do not add a test-framework dependency. Build simple PowerShell assertions and run the complete suite. Tests must cover database creation/migrations/persistence/transactions/rollback/backups, identity mappings and collisions, PTA parsing/sentinels/23:57 ordering, weighted idle math/zero-hours/28-day overlap, UTF-16 tab-delimited Missing BOL parsing, duplicate imports, API operations, PTA editing, notes/reminders/timers persistence, transition persistence, path/security checks, and clean restart.

Exercise the finished application end-to-end using representative fixture data. Fix every test failure before completion.

## Finish requirements

Do not stop until the application is complete and runnable from a clean checkout under the stated constraints. Remove dead code, ensure runtime operational `waa.db` is gitignored, document exact launch instructions in README, and verify no Python/Node/Internet/admin dependency remains.

Where a business rule is genuinely unknown—especially the meanings of the final two PTA numeric columns—preserve the raw values and build a clean extension point instead of inventing semantics.

At the end:
1. Run all tests and final end-to-end checks.
2. Create/update `CODEX_COMPLETION.md` with a concise but complete report: what was built, architecture, major files, parsers supported, tests/checks run and results, known unresolved business-rule items, and exact launch instructions.
3. Commit every completed project change, including `CODEX_COMPLETION.md`.
4. Push the finished work directly to `origin/main`.
5. Verify `git status` is clean and `origin/main` contains the final commit.
6. Stop. Do not begin unrelated improvements after completion.

The repository is finished only when the complete requested system is built, tested, documented, committed, pushed to `main`, and `CODEX_COMPLETION.md` exists on `main`.