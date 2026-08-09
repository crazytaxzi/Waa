# WAA Completion Report

## Delivered

WAA is a complete local Windows driver-operations application with six hash-routed pages: Dashboard, PTA Tracking, Workflow, Missing BOLs, Transition, and Imports/Data Quality. The shared Driver Work Card centralizes PTA history/editing, weighted idle history, BOL mentions, home-time review, on-time state, preplan review/response, routing, randomized safety coaching, chronological notes, restart-safe reminders/timers, transition selection, and activity history.

The Gothic cybernetic interface uses centralized dark gunmetal/neon tokens, angular panels, responsive tables, strong focus states, text-backed alert colors, inline SVG charts, and reduced-motion support. It contains no third-party frontend code, CDN, build step, or Internet dependency.

## Architecture and major files

- `Start-Waa.cmd` / `Start-Waa.ps1`: no-install Windows entry points.
- `src/Server.ps1`: `TcpListener` HTTP server bound only to `127.0.0.1`, static-root confinement, strict response headers, and JSON API routing.
- `src/Waa.psm1`: the single database layer, schema/migration bootstrap, SQLite safety settings, parsers, identity handling, operational queries/actions, audit, transition, and backup/restore.
- `web/index.html`, `web/styles.css`, `web/app.js`: accessible vanilla browser client, reusable work-card renderer, filtering/sorting, and SVG visualization.
- `runtime/sqlite/sqlite3.exe`: official portable SQLite 3.53.4 Windows x64 shell; archive SHA3-256 was verified against sqlite.org.
- `tests/Run-Tests.ps1`: dependency-free assertion and persistence suite.

Operational SQLite lives only at `%LOCALAPPDATA%\Waa\waa.db`. The schema covers canonical drivers and aliases, truck observations, PTA evidence, idle periods, Missing BOL history, driver work state, notes, reminders, timers, transitions, safety notes, source-preserving import batches, identity issues, settings, and audit history. It enables migrations, foreign keys on every connection, WAL, busy timeout, indexes, integrity checks, startup/manual/pre-restore backups, and recovery mode.

## Parsers

- PTA/fleet-state: 11 columns in tabular or Markdown-pipe form, blank cells, escaped underscores/pipes, alphanumeric/unknown codes, exact raw PTA and unknown numeric preservation, equipment-sentinel handling, historical snapshots, and the legitimate-driver 23:57 priority rule.
- Rolling idle: base period/truck/driver/engine/idle measurements; percentages are derived and fleet values are weighted. Zero engine hours stays No Data and period counts expose incomplete 28-day coverage.
- Missing BOL: content-based 29-column tab detection, including browser decoding of UTF-16 LE BOM input; all fields are preserved and driver code/name are used as identity evidence rather than truck.

Preview never writes. Commit reparses the supplied raw source, validates rows, records parser/source metadata and the exact source, and rejects exact duplicates by SHA-256. Ambiguous or unmatched identity evidence remains visible for explicit alias resolution.

## Validation completed

- `tests/Run-Tests.ps1`: **33/33 assertions passed** using PowerShell 7.5.2 and the official SQLite 3.53.4 Linux validation shell. Coverage includes schema creation, integrity, WAL/FKs, identity examples, PTA parsing/sentinels/23:57, rolling-idle parsing, duplicate detection, weighted math, zero hours and partial coverage, manual PTA history, durable notes/reminders/timers, transitions, 29-column BOL import, invalid-import rejection, backups, clean restart, persistence, traversal rejection, loopback binding, and offline frontend checks.
- End-to-end server exercise: launched the real TCP server, verified health, committed a representative PTA through HTTP, read the resulting driver through HTTP, served the web client, and rejected encoded traversal with HTTP 404.
- JavaScript syntax check passed. The official SQLite Windows archive hash was verified as `88b4659fe747896b853af10157316b4ade143553efb89c1c8ca7423a278dcc8b`.

## Intentionally unresolved business rules

The meanings of PTA `source_numeric_1` and `source_numeric_2` are unknown and are preserved without labels or invented semantics. Missing BOL disappearance does not imply resolution. Home-time state remains manual. Preplan source status remains distinct from reviewed/accepted/denied workflow state. Incomplete rolling coverage is not represented as a confident 28-day result.

## Launch

On the target Windows machine, double-click `Start-Waa.cmd` (or run `Start-Waa.ps1`). WAA selects loopback port 8765–8775, opens the default browser, and needs no installation, elevation, network, Python, Node, Java, package manager, or external PowerShell module.
