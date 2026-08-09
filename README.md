# WAA — Driver Operations Console

WAA is a fully local, driver-centered operations console for PTA, workflow, rolling idle, Missing BOL, transition, reminders, notes, safety coaching, audit history, imports, data quality, and backups.

## Launch on the company Windows PC

1. Copy or clone this repository to any writable folder.
2. Double-click `Start-Waa.cmd`, or run `Start-Waa.ps1` from PowerShell.
3. WAA opens the default browser at `http://127.0.0.1:8765` (or the first open port through 8775).
4. Keep the PowerShell window open while using WAA. Press `Ctrl+C` there to stop it.

No installation, administrator access, Internet, Python, Node, Java, module download, browser extension, or cloud account is needed. Runtime data is in `%LOCALAPPDATA%\Waa\waa.db`; safe backups are in `%LOCALAPPDATA%\Waa\backups`. The portable SQLite shell is bundled under `runtime\sqlite`.

## Report intake

WAA treats the Windows **Downloads** folder as the automatic intake location for two report families only:

- the newest **Rolling 7-Day** idle report
- the newest **Missing BOL / Order Details Missing BOL** report

WAA resolves the real Windows Downloads known folder when possible, including redirected corporate profiles. On startup, while the app is being used, and when **Scan Downloads Now** is pressed, WAA finds only the newest matching file for each report family, validates its actual content, copies the source into `%LOCALAPPDATA%\Waa\reports`, imports new data into SQLite, and leaves the original file untouched in Downloads. SHA-256/content checks prevent repeat imports of an already-current report.

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

Normal Tab navigation follows that order. Call-flow answers auto-save into a persistent `driver_call_sessions` record scoped to the driver's current truck/PTA work cycle, so a new operating cycle does not blindly inherit stale phone-call answers.

Notes are intentionally separate from the structured call questions. The sticky **Remember This** rail is a conversational scratchpad for short, useful notes. Press `Alt+N` while a Driver Work Card is open to jump to it; Enter saves the note and Shift+Enter adds a line break.

## Dashboard and charts

The UI uses the same neon operations-console design throughout the dashboard, queues, imports, transition screen, and Driver Work Card. Charts are custom SVG components with actual connected trend lines, area glow, axes, keyboard-focusable/hoverable inspection bands, crosshairs, tooltips, and horizontal scrolling as history grows. The same chart component is reused for fleet history and driver idle coaching.

## Architecture and security

- `Start-Waa.ps1` — dependency-free launcher
- `src/Server.ps1` — loopback-only HTTP/static/API server and service coordinator
- `src/Waa.psm1` — centralized SQLite, driver/workflow domain, PTA parsing, dashboard queries, audit, backup/restore
- `src/Conversation.ps1` — persisted driver call-flow sessions and call-cycle state
- `src/ReportParsing.ps1` — shared tab/CSV row parsing and date normalization
- `src/ReportIntake.ps1` — Downloads discovery, native XLSX/OpenXML reading, report adapters, managed copies, Rolling 7-Day and Missing BOL ingestion
- `web/` — vanilla HTML/CSS/ES-module client and interactive SVG chart system
- `tests/Run-Tests.ps1` — standalone PowerShell assertion suite including XLSX intake coverage

The server binds `System.Net.IPAddress.Loopback` only. Static paths are canonicalized, imported content is data only, SQL values are escaped before statement construction, CORS is loopback-only, and a strict CSP blocks outside scripts, styles, frames, and connections. SQLite enables foreign keys, WAL, busy timeout, indexes and transactions. Startup performs an integrity check and enters read-only recovery mode on failure.

## Run the tests

On Windows, from a PowerShell prompt in the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

The suite uses no external test framework and creates its database under the temporary directory.

## Identity and business rules

Drivers are canonical entities. Trucks are historical observations only. Aliases preserve PTA codes, dispatch codes, and full names independently. Automatic file reports use dispatch codes/full names and generated PTA-style name keys as identity evidence; uncertain matches must remain visible rather than being guessed. Final PTA numeric columns are stored verbatim as `source_numeric_1` and `source_numeric_2` because their meaning is unknown. Missing BOL items remain historical until explicitly handled; a missing row in a newer report is not treated as resolution.
