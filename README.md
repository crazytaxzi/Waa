# WAA — Driver Operations Console

WAA is a fully local, driver-centered operations console for PTA, workflow, rolling idle, Missing BOL, transition, reminders, notes, safety coaching, audit history, imports, data quality, and backups.

The **Notes & Reminders** tab is a separate driver-specific organizer: choose the canonical driver, capture or delete a note or dated reminder, search across the fleet, and complete reminders without opening the call flow. The same items can be deleted from the Driver Work Card. The **Daily Review** tab shows one summary card per driver for the selected day instead of flooding the page with every field save. Opening a summary shows that driver's chronological actions, concise action counts, individual delete controls, and a direct link to the Driver Work Card. Automatic identity reconciliation never appears as driver work, and exact duplicate card events are collapsed. **Clean Up Review** removes old identity-system noise and duplicate audit copies across all dates without deleting notes, reminders, imports, driver work, or unique actions. Deleting an individual review record removes only that history entry; it does not reverse the underlying operational action.

## Launch on the company Windows PC

1. Copy or clone this repository to any writable folder.
2. Double-click `Start-Waa.cmd`, or run `Start-Waa.ps1` from PowerShell.
3. WAA opens the default browser at `http://127.0.0.1:8765` (or the first open port through 8775).
4. Keep the PowerShell window open while using WAA. Press `Ctrl+C` there to stop it.

No installation, administrator access, Internet, Python, Node, Java, module download, browser extension, or cloud account is needed. Runtime data is in `%LOCALAPPDATA%\Waa`: LMDB live state under `live\`, durable SQLite history in `waa.db`, and safe backups under `backups\`. Both native runtimes are bundled.

## Report intake

WAA treats the Windows **Downloads** folder as the automatic intake location for two report families only:

- up to eight recent **Rolling 7-Day** idle reports, enough to backfill 28-day history
- the newest **Missing BOL / Order Details Missing BOL** report

WAA resolves the real Windows Downloads known folder when possible, including redirected corporate profiles. The loopback server and browser now start before automatic report maintenance. While the app is open, WAA checks the eight most recent idle reports oldest-first so an existing four-week history becomes usable immediately. Missing BOL still uses only its newest report. Valid sources are copied into `%LOCALAPPDATA%\Waa\reports`; originals remain untouched. A lightweight file signature skips unchanged history scans, and SHA-256 content checks prevent duplicate imports.

Startup backups, periodic scans, and identity reconciliation run in a background PowerShell runspace so they cannot delay opening the console or stall normal browser requests. The header briefly shows **LOOPBACK · SYNCING** and the active view refreshes when maintenance finishes. Identity repair is versioned and change-driven: unchanged launches skip it entirely, normal imports use normalized SQLite evidence, and historical raw reports are replayed only once when a new repair migration requires them. Manual scans remain explicit and report their result immediately.

Supported automatic file forms are `.csv`, `.txt`, and `.xlsx`. XLSX support is native: PowerShell reads the OpenXML ZIP/XML package directly using built-in .NET classes. Excel does not need to be installed or running. The reader searches worksheets for the expected report headers rather than assuming the first worksheet or trusting the filename.

Missing BOL intake accepts report headers such as `Order #`, `Empty Call Date`, `Origin City St`, `Destination City St`, `Rev Type`, `Loaded Miles`, `Last Dispatch Driver cd`, and `Last Dispatch Driver nm`. Excel serial dates are normalized before import. Rolling 7-Day intake likewise normalizes workbook dates and stores the base engine/idle hours used for dashboard calculations.

**PTA is deliberately different:** it is copy/paste only. Open **Imports / Data Quality**, paste the 11-column PTA/fleet-state table, preview it, then commit the snapshot. WAA never scans Downloads for PTA files.

## Driver call flow

The Driver Work Card is designed to follow a real phone conversation rather than expose every field at once. The default order is:

1. Fuel and immediate needs
2. ETA and timing
3. Idle coaching
4. Help on the load, including preplan/routing
5. Home time and schedule
6. Missing BOL/admin close-out
7. Safety and wrap-up

Use **Start Queue** to open the first visible pending driver. The card shows one task at a time, resumes at the first unfinished step, and provides Back/Next controls plus `Alt+Left` / `Alt+Right` shortcuts. Step navigation advances immediately while its LMDB autosave completes; finishing or changing drivers still drains pending saves. Call-flow answers are scoped to the driver's current truck/PTA work cycle, so a new operating cycle does not inherit stale answers. Workflow defaults to pending calls, keeps completed calls separately filterable, and returns a driver to Pending when a new PTA cycle begins.

Notes are intentionally separate from the structured call questions. The sticky **Remember This** rail is a conversational scratchpad for short, useful notes. Press `Alt+N` while a Driver Work Card is open to jump to it; Enter saves the note and Shift+Enter adds a line break.

## Manual truck assignment

Drivers without a current truck association are surfaced by the **Needs Truck** filter on Workflow. Assign the truck directly in that row or from the shared Driver Work Card. WAA validates and normalizes the truck number, appends a `manual` observation to `truck_history`, and audits the action against the canonical driver. The assignment never becomes driver identity and cannot silently replace an existing current truck; later imported assignment evidence remains historical in the same unified model.

## Transition synchronization

Selecting **Send to Transition** or changing that driver's transition note immediately synchronizes the persisted handoff line as `<truck> - <driver name> : <transition note>`. Generated drafts remain ordered by truck. If the transition text was manually edited, WAA adds, replaces, or removes only the affected driver's managed line and preserves all unrelated manual text. **Regenerate** remains available to intentionally rebuild the complete standard draft.

## Dashboard and charts

The UI uses the same neon operations-console design throughout the dashboard, queues, imports, transition screen, and Driver Work Card. Charts are custom SVG components with actual connected trend lines, area glow, axes, keyboard-focusable/hoverable inspection bands, crosshairs, tooltips, and horizontal scrolling as history grows. The same chart component is reused for fleet history and driver idle coaching.

The two 7-day Top 5 lists exclude exact 0% and 100% readings as likely telemetry/reporting edge cases. Those records remain stored and visible, and the safeguard does not filter, clamp, or otherwise alter the fleet or driver weighted 28-day calculations.

The **Above 50% Coached** dashboard card measures drivers whose latest Rolling 7-Day idle is above 50% and who have a non-empty Idle Coaching plan saved in any Driver Card call session. It shows the coached percentage plus the exact coached/eligible driver counts. This coaching metric does not change idle measurements or Top 5 membership.

Daily Review is intentionally driver-specific. System-level audit events such as imports, backups, transition regeneration, automatic identity evidence/merges, and other non-driver messages remain outside the review list. Repeated identity scans no longer create driver audit events.

## Architecture and security

- `Start-Waa.ps1` — dependency-free launcher
- `src/Server.ps1` — loopback-only HTTP/static/API server and service coordinator
- `src/Waa.psm1` — centralized SQLite, driver/workflow domain, PTA parsing, dashboard queries, audit, backup/restore
- `src/LiveStore.ps1` — bundled LMDB interop, live domain state, recovery, and atomic SQLite checkpoints
- `src/Conversation.ps1` — persisted driver call-flow sessions and call-cycle state
- `src/ReportParsing.ps1` — shared tab/CSV row parsing and date normalization
- `src/ReportIntake.ps1` — Downloads discovery, native XLSX/OpenXML reading, report adapters, managed copies, Rolling 7-Day and Missing BOL ingestion
- `web/` — vanilla HTML/CSS/ES-module client and interactive SVG chart system
- `tests/Run-Tests.ps1` — standalone PowerShell assertion suite including XLSX intake coverage

The server binds `System.Net.IPAddress.Loopback` only. Static paths are canonicalized, imported content is data only, SQL values are escaped before statement construction, CORS is loopback-only, and a strict CSP blocks outside scripts, styles, frames, and connections. SQLite enables foreign keys, WAL, busy timeout, indexes and transactions. Startup performs an integrity check and enters read-only recovery mode on failure.

LMDB is authoritative for latency-sensitive calls, Driver Work Card fields, notes, reminders, timers, and transition drafts. Each mutation is one atomic embedded write with a monotonic revision and pending audit event. SQLite remains authoritative for identity, imported evidence, idle/BOL history, durable audit, reporting, and backups. Dirty LMDB revisions checkpoint to SQLite in one transaction after a short interval, at a batch threshold, before backup/restore and identity repair, during Daily Review reads, and on clean shutdown. On restart, any LMDB revision ahead of SQLite is replayed before requests are served. See `LMDB_SQLITE_ARCHITECTURE.md`.

Hot screens use purpose-built indexed queries. The centralized database layer keeps one long-lived SQLite shell session open for the lifetime of WAA, sends every query through that session under a synchronization lock, and cleans it up when the module closes. This removes the process startup/teardown pause that previously occurred on every database call while retaining the portable official `sqlite3.exe` runtime. The session automatically restarts if SQLite exits unexpectedly. Driver-card data and the current call session now arrive through one context endpoint; redundant standalone card/conversation reads were removed. Conversation writes use consolidated SQL, ordinary field mutations return only `{ok}`, and follow-up mutations return only notes/reminders/timers instead of rebuilding history, charts, BOLs, work state, and audit data.

Browser routes use short-lived in-memory caching with explicit mutation invalidation, cancellable reads, delegated interaction handlers, render-once table filtering, queue-aware driver progression, and contained off-screen layout. Driver-card reads cancel stale requests, only one workflow step is laid out at a time, step changes advance immediately after queuing their autosave, and driver changes still drain pending saves. Driver Work Card listeners have an explicit lifecycle: reopening or refreshing a card aborts the prior listener scope before binding the new one, preventing duplicate saves. Create controls also lock while their request is active. Static assets are versioned and held in memory by the loopback server; HTML revalidates so an upgrade cannot leave stale listener code in the browser, while API responses remain `no-store`. Continuous decorative animations, SVG Gaussian blur, full-window backdrop filters, and large live blur effects were removed to avoid idle GPU work and expensive recomposition on modest PCs. Card responses no longer transport unused audit history, and the organizer now reads only the compact driver identity fields its selector needs.

Step-to-step card navigation advances immediately after queuing the field's autosave. Queue changes and **Finish Call & Next Driver** still drain pending saves before changing drivers.

## Run the tests

On Windows, from a PowerShell prompt in the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

The suite uses no external test framework and creates its database under the temporary directory.

The repository also includes `tests/Identity.Tests.ps1` for cross-report canonical identity behavior and `tests/Measure-PtaPerformance.ps1` for the bulk PTA pipeline.

## Identity and business rules

Drivers are canonical entities. Trucks are historical observations only. Aliases preserve PTA codes, dispatch codes, and full names independently. Automatic file reports use dispatch codes/full names and generated PTA-style name keys as identity evidence; uncertain matches must remain visible rather than being guessed. Final PTA numeric columns are stored verbatim as `source_numeric_1` and `source_numeric_2` because their meaning is unknown. Missing BOL items remain historical until explicitly handled; a missing row in a newer report is not treated as resolution.

Observed PTA-code families can contain both a short derived form and a valid extended form. When exactly one real driver name is structurally compatible, codes such as `JONESI` and `JONESIRA` resolve to the same Ira Jones record even if the associated truck changed between reports. If a prefix is compatible with more than one real driver, WAA refuses the automatic merge and leaves an identity issue for review.
