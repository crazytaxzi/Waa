# WAA — Driver Operations Console

WAA is a fully local, driver-centered operations console for PTA, workflow, rolling idle, Missing BOL, transition, reminders, timers, notes, safety coaching, audit history, imports, data quality, and backups.

## Launch on the company Windows PC

1. Copy or clone this repository to any writable folder.
2. Double-click `Start-Waa.cmd`, or right-click `Start-Waa.ps1` and choose **Run with PowerShell**.
3. WAA opens the default browser at `http://127.0.0.1:8765` (or the first open port through 8775).
4. Keep the PowerShell window open while using WAA. Press `Ctrl+C` there to stop it.

No installation, administrator access, Internet, Python, Node, Java, module download, browser extension, or cloud account is needed. Runtime data is in `%LOCALAPPDATA%\Waa\waa.db`; safe backups are in `%LOCALAPPDATA%\Waa\backups`. The official portable SQLite shell is bundled under `runtime\sqlite`.

If script execution is restricted, launch from Command Prompt with `Start-Waa.cmd`; it applies `ExecutionPolicy Bypass` only to this process and changes no machine policy.

## Report intake

WAA treats the Windows **Downloads** folder as the automatic intake location for two report families only:

- the newest **Rolling 7-Day** idle report
- the newest **Missing BOL / Order Details Missing BOL** report

WAA resolves the real Windows Downloads known folder when possible, including redirected corporate profiles. On startup, while the app is being used, and when **Scan Downloads Now** is pressed, WAA finds only the newest matching file for each report family, validates its actual content, copies the source into its managed report area under `%LOCALAPPDATA%\Waa\reports`, imports new data into SQLite, and leaves the original file untouched in Downloads. SHA-256/content checks prevent repeat imports of an already-current report.

Supported automatic file forms are `.csv`, `.txt`, and `.xlsx`. XLSX support is native: PowerShell reads the OpenXML ZIP/XML package directly using built-in .NET classes. Excel does not need to be running or installed for WAA to read the workbook. The reader searches worksheets for the expected report headers rather than assuming the first worksheet or trusting the filename.

Missing BOL intake accepts the real report header names such as `Order #`, `Empty Call Date`, `Origin City St`, `Destination City St`, `Rev Type`, `Loaded Miles`, `Last Dispatch Driver cd`, and `Last Dispatch Driver nm`. Excel serial dates are normalized before import. Rolling 7-Day intake likewise normalizes workbook dates and stores the base engine/idle hours used for the dashboard calculations.

**PTA is deliberately different:** it is copy/paste only. Open **Imports / Data Quality**, paste the 11-column PTA/fleet-state table, preview it, then commit the snapshot. WAA does not scan Downloads for PTA files.

Open identity exceptions are listed on the same page; double-click one to enter the canonical driver ID and persist a confirmed alias. Backup rows can likewise be double-clicked to restore them after confirmation; WAA first creates a pre-restore backup.

## Architecture and security

- `Start-Waa.ps1` — dependency-free launcher
- `src/Server.ps1` — loopback-only HTTP/static/API server and report-intake coordinator
- `src/Waa.psm1` — centralized SQLite, driver/workflow domain, PTA parsing, dashboard queries, audit, backup/restore
- `src/ReportParsing.ps1` — shared tab/CSV row parsing and date normalization
- `src/ReportIntake.ps1` — Downloads discovery, native XLSX/OpenXML reading, report adapters, managed copies, Rolling 7-Day and Missing BOL ingestion
- `web/` — vanilla HTML/CSS/ES-module client and SVG charts
- `tests/Run-Tests.ps1` — standalone PowerShell assertion suite including XLSX intake coverage

The server binds `System.Net.IPAddress.Loopback` only. Static paths are canonicalized, imported content is data only, SQL values are escaped before statement construction, CORS is loopback-only, and a strict CSP blocks outside scripts, styles, frames, and connections. SQLite enables foreign keys, WAL, busy timeout, indexes and transactions. Startup performs an integrity check and enters read-only recovery mode on failure.

## Run the tests

On Windows, from a PowerShell prompt in the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

The suite uses no test framework and creates its database under the temporary directory. It covers driver identity, SQLite row-shape compatibility, PTA logic, weighted idle calculations, native XLSX/OpenXML Missing BOL extraction, Excel serial dates, newest-download selection, persistence, backups, and loopback security.

## Identity and business rules

Drivers are canonical entities. Trucks are historical observations only. Aliases preserve PTA codes, dispatch codes, and full names independently. Automatic file reports use dispatch codes/full names and the generated PTA-style name key as identity evidence; uncertain matches must remain visible rather than being guessed. Final PTA numeric columns are stored verbatim as `source_numeric_1` and `source_numeric_2` because their meaning is unknown. Missing BOL items remain historical until explicitly handled; a missing row in a newer report is not treated as resolution.