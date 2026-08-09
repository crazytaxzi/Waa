# WAA — Driver Operations Console

WAA is a fully local, driver-centered operations console for PTA, workflow, rolling idle, Missing BOL, transition, reminders, timers, notes, safety coaching, audit history, imports, data quality, and backups.

## Launch on the company Windows PC

1. Copy or clone this repository to any writable folder.
2. Double-click `Start-Waa.cmd`, or right-click `Start-Waa.ps1` and choose **Run with PowerShell**.
3. WAA opens the default browser at `http://127.0.0.1:8765` (or the first open port through 8775).
4. Keep the PowerShell window open while using WAA. Press `Ctrl+C` there to stop it.

No installation, administrator access, Internet, Python, Node, Java, module download, browser extension, or cloud account is needed. Runtime data is in `%LOCALAPPDATA%\Waa\waa.db`; safe backups are in `%LOCALAPPDATA%\Waa\backups`. The official portable SQLite shell is bundled under `runtime\sqlite`.

If script execution is restricted, launch from Command Prompt with `Start-Waa.cmd`; it applies `ExecutionPolicy Bypass` only to this process and changes no machine policy.

## Imports

Open **Imports / Data Quality**, paste text or select a local file, then preview and confirm. WAA detects:

- 11-column PTA/fleet-state tab or Markdown tables
- rolling idle exports (only `Idle %` measurement rows, preserving base engine/idle hours)
- UTF-16 BOM, tab-delimited 29-column Missing BOL reports

Preview is read-only. Commit reparses the original source server-side and uses SHA-256 duplicate detection. Exact raw evidence is retained in SQLite. Unknown fields and codes remain unknown.

Open identity exceptions are listed on the same page; double-click one to enter the canonical driver ID and persist a confirmed alias. Backup rows can likewise be double-clicked to restore them after confirmation; WAA first creates a pre-restore backup.

## Architecture and security

- `Start-Waa.ps1` — dependency-free launcher
- `src/Server.ps1` — loopback-only HTTP/static/API server
- `src/Waa.psm1` — centralized SQLite, schema, parsers, identity, workflows, audit, backup/restore
- `web/` — vanilla HTML/CSS/ES-module client and SVG charts
- `tests/Run-Tests.ps1` — standalone PowerShell assertion suite

The server binds `System.Net.IPAddress.Loopback` only. Static paths are canonicalized, imported content is data only, SQL values are escaped centrally, CORS is loopback-only, and a strict CSP blocks outside scripts, styles, frames, and connections. SQLite enables foreign keys, WAL, busy timeout, indexes and transactions. Startup performs an integrity check and enters read-only recovery mode on failure.

## Run the tests

On Windows, from a PowerShell prompt in the repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

The suite uses no test framework and creates its database under the temporary directory.


## Identity and business rules

Drivers are canonical entities. Trucks are historical observations only. Aliases preserve PTA codes, dispatch codes, and full names independently; uncertain matches surface in Data Quality. Final PTA numeric columns are stored verbatim as `source_numeric_1` and `source_numeric_2` because their meaning is unknown. Missing BOL items remain historical until explicitly handled; a missing row in a newer report is not treated as resolution.
