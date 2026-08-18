# WAA Completion Report

## Delivered

The 2026-08-18 DOT follow-up exposes the already-normalized T2 Date field as a sortable **Last Ping** column in the DOT spreadsheet. The source value remains unchanged in storage/API; this is a presentation and accountability correction only.

The 2026-08-17 DOT correction fixes the inspection-date interpretation. `Last DOT Date` is the date the inspection was completed, not its due date. WAA calculates due date as inspection + 365 days and priority as days since inspection minus 365: positive values are days overdue, zero is due today, and negative values are days remaining. Default DOT sorting is most overdue first. Source dates and exported `Days Since Last DOT` values remain preserved unchanged as evidence.

The 2026-08-17 idle-coaching correction changes the coaching threshold to the actual operating rule: **coach only when complete weighted 28-day idle exceeds 50%**. Dashboard eligibility, the Idle Coaching attention queue, Driver Work Card step behavior, and server-side conversation writes all use the weighted 28-day result. Drivers at 50% or lower, or without four consecutive valid weekly reports, are not coaching targets. New coaching records persist the weighted percentage, period end, and `weighted_28d` basis; legacy snapshot rows remain distinguishable as prior 7-day history.

The 2026-08-16 optimization pass adds first-class **DOT trailer accountability**. `DOT Table` CSV/text exports are detected in Downloads, source-preserved in import batches, and normalized from repeated Tableau `Measure Names` rows into one trailer record per import. The DOT page is a dense sortable spreadsheet with search, status/KMA filters, persistent per-trailer Hide/Unhide preferences, and customer-site distance mapping from ZIP 83501. The app deliberately leaves mileage unresolved when the report provides no trustworthy address/ZIP instead of fabricating distance from KMA.

This pass also fixed two target-Windows reliability defects discovered during clean validation. LMDB platform selection no longer trusts the optional `$env:OS` variable; it uses .NET's actual OS platform, preventing Windows PowerShell launched without that environment variable from trying to load `liblmdb.so`. The native XLSX test harness now explicitly loads both `System.IO.Compression` and `System.IO.Compression.FileSystem`, making its `ZipArchive` fixture generator portable on Windows PowerShell 5.1.

WAA now runs a fully integrated LMDB + SQLite hybrid persistence core. Bundled LMDB is the authoritative low-latency store for Driver Work Card state, call sessions, notes, reminders, timers, and transition drafts; SQLite remains the durable authority for imported evidence, identity, idle/BOL history, audit, reports, and backups. Live mutations use atomic revisioned LMDB transactions and leave the UI request path without a SQLite round trip. Batched SQLite checkpoints are idempotent, forced before backup/restore and identity repair, and replayed automatically on restart. A recovery test confirmed an uncheckpointed LMDB note moved from SQLite count 0 to 1 during restart recovery with no remaining dirty state.

Daily Review is grouped by driver and day. Its main page renders one compact summary card per driver with total actions and the leading action categories. The summary popup provides the full chronological detail only when requested, preserves per-record deletion, and links directly into that driver's Work Card. This keeps high-volume field autosaves auditable without letting them overwhelm the daily workflow.

Identity migration version 4 reconciles unique, structurally compatible PTA-code families without relying on a current truck match. Existing `JONESI` and `JONESIRA` placeholders, aliases, PTA history, and operational records migrate to the canonical Ira Jones driver even when the truck changed between snapshots. Ambiguous short prefixes remain deliberately unmerged.

Driver navigation is consistent across the application. Dashboard rank buttons and the best-idle metric, Workflow/PTA rows, Missing BOL rows, organizer driver labels, and Daily Review buttons all open the same Driver Work Card. The delegated click guard now accepts an interactive element when that element is itself the driver opener. Driver-card **Next Step** advances immediately after focus queues the LMDB autosave, so a slow or stale request cannot trap the operator on the current task; changing drivers and finishing a call still drain pending saves.

WAA is a complete local Windows driver-operations application with ten hash-routed pages: Dashboard, PTA Tracking, Workflow, Idle Coaching, Missing BOLs, DOT Trailers, Notes & Reminders, Daily Review, Transition, and Imports/Data Quality. The shared Driver Work Card centralizes PTA history/editing, weighted idle history, BOL mentions, home-time review, on-time state, preplan review/response, routing, randomized safety coaching, chronological notes, restart-safe reminders/timers, transition selection, and activity history.

The application now also includes two first-class pages. **Notes & Reminders** is a fleet-wide organizer that requires a canonical driver for every item and supports capture, search, filtering, due-state display, and completion. **Daily Review** presents the activity trail as a local-day chronological record, keeps driver actions attributed to the canonical driver/current truck, supports driver filtering, and opens the shared Driver Work Card directly.

Notes and reminders now have ownership-checked manual deletion from both the organizer and shared Driver Work Card. Daily Review records have explicit deletion through the core activity API; deleting a record removes that history entry without pretending to undo the original operational change. The Driver Work Card event lifecycle was corrected so each render aborts its previous delegated listener scope, and create controls lock during active requests. This eliminates the accumulated listeners that could previously save one click multiple times after reopening the card.

Manual truck assignment is part of the canonical Workflow, PTA table, and Driver Work Card. Unassigned drivers can be filtered and assigned inline, and any assigned driver can be deliberately moved after confirmation. Each validated assignment appends to standard `truck_history` with source `manual`, immediately feeds every current-truck query, and creates a driver-specific audit event. Previous assignments remain intact, same-truck no-ops are rejected, and trucks remain time-dependent observations rather than driver identity.

The Gothic cybernetic interface uses centralized dark gunmetal/neon tokens, angular panels, responsive tables, strong focus states, text-backed alert colors, inline SVG charts, and reduced-motion support. It contains no third-party frontend code, CDN, build step, or Internet dependency.

Performance work is integrated into the existing architecture: the database layer now maintains one synchronized, self-restarting SQLite shell session for the application lifetime instead of launching `sqlite3.exe` for every query. It preserves the portable-runtime constraint and disposes the child process with the module. In a same-machine 100-query comparison, this reduced database transport time from 1010.8 ms to 308.2 ms (3.28x faster). Startup now binds the loopback listener and opens the browser before automatic backup, Downloads scanning, or identity maintenance. Those tasks run in a background runspace; health state reports progress and the browser invalidates and refreshes operational caches when it completes. Identity repair uses a persisted algorithm version, skips unchanged launches completely, runs once after a group of imports, avoids routine historical raw-report replay, and avoids rewriting already-current observed PTA aliases. A 1,000-driver reproduction that previously spent 20.4 seconds in first repair and 11.3 seconds on every unchanged repair is therefore removed from the foreground startup path. Startup backups still retain the newest ten automatic copies. The Driver Work Card is assembled by one SQLite JSON query; current-driver lookups are targeted; dashboard latest-row ties and true contiguous weighted 28-day history are handled correctly; driver/audit/organizer indexes are migrated in place; static assets are memory-cached; and the byte-accurate HTTP reader enforces header/body limits and timeouts. The client uses cancellable cached route reads, explicit invalidation, render-once table filtering, delegated table/card/chart interactions, partial card-section refreshes, and CSS layout/paint containment.

The Workflow is now an actual resumable work queue rather than a flat table. Every truck/PTA call cycle stores a completion timestamp in the core schema, Workflow opens on pending calls, **Start Queue** opens the first visible driver, completed calls remain reviewable, and a new PTA automatically creates a new pending cycle. The shared card presents one of seven tasks at a time, resumes at the first incomplete task, supports direct step and previous/next-driver navigation, drains outstanding auto-saves before moving, cancels stale card reads, and offers **Finish Call & Next Driver**. Existing locale-formatted cycle keys are migrated to stable `yyyy-MM-dd` keys without discarding their sessions.

The final low-end-PC pass removed unused audit-history payloads from card context, capped its visible notes at the eight the card renders, replaced the organizer's full operational driver query with a compact selector query, removed SVG Gaussian blur, and prevents hidden card steps from participating in layout or paint. Daily Review translates call fields into readable action language instead of displaying internal column names. A route/reference audit confirmed every browser API reference maps to a live server route and no standalone/redundant card read endpoint remains.

The multi-pass endpoint and reference audit removed the redundant standalone Driver Card and conversation GET routes and replaced them with one driver-context read. Conversation schema setup no longer runs on every read, conversation updates use a consolidated transaction, routine mutations return a minimal acknowledgement, and follow-up changes return only the three affected lists. Timers, timer deletion, reminder snoozing, on-time status/reason, and their Daily Review labels are now connected to the shared card instead of existing only as unreachable schema/action fields. All remaining browser API references map to a live server route.

Rolling reports can contain many historical weekly records in one file; WAA imports all of those rows, so one current report is sufficient when it carries the history. This was verified against the available real CSV: 920 valid idle periods, 92 drivers, 10 distinct weeks, and valid weighted 28-day results for all 92 drivers. Intake can also examine up to eight recent candidate files as fallback, imports them oldest-first, performs identity repair once per batch, and records a lightweight directory signature so unchanged periodic scans do no parsing or hashing work. Dashboard coverage explains whether it has 1–4 weeks, a non-seven-day period, a gap, or zero engine hours. Four consecutive valid weekly records produce the required weighted `SUM(idle)/SUM(engine)` result.

The 7-day Top 5 lists apply a display-only telemetry safeguard: exact 0% and 100% weekly readings are excluded from both comparative rankings. The original measurements stay in SQLite and remain available elsewhere. Fleet history, per-driver history, 28-day coverage, and weighted 28-day values are explicitly outside this filter.

The main dashboard 28-day chart now explicitly binds the shared chart renderer to `p28`; it previously inherited the renderer's `p7` default and therefore displayed an empty chart despite valid API data. Successful report scans and PTA commits also invalidate dashboard/driver caches immediately, so the next navigation cannot show a stale pre-import snapshot.

Driver Card transition selection now synchronizes the persisted transition draft immediately. Generated drafts use `<truck> - <driver name> : <transition note>` lines in truck order. For manually edited drafts, synchronization surgically replaces only the affected driver's generated line, preserving unrelated manual content instead of requiring Regenerate or overwriting the draft.

The dashboard includes **28D >50% Coached**: the percentage of drivers whose complete weighted 28-day idle exceeds 50% and who have a coaching plan captured against that same current weighted period. The Idle Coaching page uses the identical eligibility rule and sorts by weighted 28-day percentage. New coaching snapshots explicitly store their basis so historical 7-day conversations cannot be mistaken for current 28-day coaching evidence.

Daily Review now returns only audit events attached to a valid canonical driver. System/import/backup and other non-driver audit messages remain persisted but no longer clutter the driver activity review.

Daily Review cleanup now addresses historical audit inflation at its source. Repeated identity reconciliation previously inserted a fresh `identity_evidence` driver event on every pass, which could create thousands of entries despite no user action; that producer was removed, and automatic identity merges are now classified as identity-system events. The review query excludes known identity noise and collapses exact same-second card duplicates before transport. **Clean Up Review** permanently removes existing identity noise and redundant audit copies across all dates after confirmation, preserves one canonical copy of a meaningful action, leaves all operational tables untouched, and runs SQLite planner optimization afterward.

Rendering was simplified for low-end PCs: continuous ambient/signal/pulse animation, full-window backdrop blur, SVG point drop shadows, and large live blur layers were removed while retaining the dark neon visual hierarchy. Copy such as “Heroes in Training,” “Steal the good habits,” and the invented organization expansion was replaced with direct operational language.

## Architecture and major files

- `Start-Waa.cmd` / `Start-Waa.ps1`: no-install Windows entry points.
- `src/Server.ps1`: `TcpListener` HTTP server bound only to `127.0.0.1`, static-root confinement, strict response headers, and JSON API routing.
- `src/Waa.psm1`: the single database layer, schema/migration bootstrap, SQLite safety settings, parsers, identity handling, operational queries/actions, audit, transition, and backup/restore.
- `src/LiveStore.ps1`: LMDB native interop, live entity model, revisions, checkpointing, hydration, health, and restart recovery.
- `src/DotTracking.ps1`: DOT source normalization, historical trailer snapshots, Last-DOT age queries, distance mappings, and persistent visibility preferences.
- `web/index.html`, `web/styles.css`, `web/app.js`, `web/dot.js`: accessible vanilla browser client; DOT is isolated as its own spreadsheet module instead of further enlarging the main application file.
- `runtime/sqlite/sqlite3.exe`: official portable SQLite 3.53.4 Windows x64 shell; archive SHA3-256 was verified against sqlite.org.
- `runtime/lmdb/lmdb.dll`: pinned LMDB 1.0.1 Windows x64 runtime, with license, provenance, and a Linux validation build.
- `tests/Run-Tests.ps1`: dependency-free assertion and persistence suite.

Operational SQLite lives only at `%LOCALAPPDATA%\Waa\waa.db`. The schema covers canonical drivers and aliases, truck observations, PTA evidence, idle periods, Missing BOL history, driver work state, notes, reminders, timers, transitions, safety notes, source-preserving import batches, identity issues, settings, and audit history. It enables migrations, foreign keys on every connection, WAL, busy timeout, indexes, integrity checks, startup/manual/pre-restore backups, and recovery mode.

## Parsers

- PTA/fleet-state: 11 columns in tabular or Markdown-pipe form, blank cells, escaped underscores/pipes, alphanumeric/unknown codes, exact raw PTA and unknown numeric preservation, equipment-sentinel handling, historical snapshots, and the legitimate-driver 23:57 priority rule.
- Rolling idle: base period/truck/driver/engine/idle measurements; percentages are derived and fleet values are weighted. Zero engine hours stays No Data and period counts expose incomplete 28-day coverage.
- Missing BOL: content-based 29-column tab detection, including browser decoding of UTF-16 LE BOM input; all fields are preserved and driver code/name are used as identity evidence rather than truck.
- DOT trailers: CSV/tab export detection, repeated Tableau measure-row collapse, leading-zero trailer preservation, normalized Last DOT/T2 dates, preserved source day-count evidence, and customer-key normalization for persistent distance mapping.

Preview never writes. Commit reparses the supplied raw source, validates rows, records parser/source metadata and the exact source, and rejects exact duplicates by SHA-256. Ambiguous or unmatched identity evidence remains visible for explicit alias resolution.

## Validation completed - 2026-08-17 weighted 28-day coaching correction

- `tests/Run-Tests.ps1`: **101/101 assertions passed**, including a regression where latest 7-day idle is 20% but weighted 28-day idle is 65%; that driver remains coaching-eligible because the 28-day result is the source of truth.
- The same regression verifies coaching falls out at exactly 50% weighted 28-day and the backend refuses a new idle plan at or below the threshold.
- New coaching snapshots persist `idle_percent_snapshot`, `idle_period_end_snapshot`, and `idle_snapshot_basis=weighted_28d`; legacy rows remain explicitly labeled as prior 7-day basis instead of being reinterpreted.
- Browser and PowerShell syntax checks pass. `tests/Dot.Tests.ps1` passed, `tests/Identity.Tests.ps1` passed 18/18, and the 500-row PTA pipeline remained `responsive` at 202.0 ms total (15.5 ms parse, 7.0 ms database).

## Validation completed — 2026-08-16 DOT / Windows reliability revision

- `tests/Run-Tests.ps1`: **99/99 assertions passed** on the target GamePC under Windows PowerShell 5.1 after correcting LMDB platform detection and explicit ZIP assembly loading.
- DOT parsing was validated locally against the supplied real export: 30 Tableau measure rows collapsed to 15 unique trailers without losing leading-zero IDs. The committed `tests/Dot.Tests.ps1` regression fixture is deliberately synthetic because the repository is public; it verifies the same measure-row collapse, Last-DOT age recalculation, visibility persistence, customer distance fan-out, and Downloads intake behavior without publishing operational data.
- Real loopback server smoke test passed on `127.0.0.1:8765`: startup integrity was `ok`; DOT automatic scan, `/api/dot`, `dot.js` static delivery, Hide/Unhide, and customer-distance persistence all completed successfully with 15 rows.
- `tests/Identity.Tests.ps1`: **18/18 assertions passed** on GamePC. `tests/Measure-PtaPerformance.ps1`: **500 rows in 210.8 ms total** (16.5 ms parse, 6.8 ms database), result `responsive`; its teardown was also corrected to close the persistent SQLite module before removing the temporary database.
- JavaScript syntax checks, PowerShell parser checks, and `git diff --check` were run on the final revision before publication.

## Validation completed — 2026-08-09 LMDB hybrid revision

- `tests/Run-Tests.ps1`: **86/86 assertions passed** against the hybrid core, including reassignment history and grouped Daily Review wiring.
- All PowerShell source/test files passed the PowerShell parser; `web/app.js` passed `node --check`; `git diff --check` passed.
- Loopback end-to-end checks returned HTTP 200 for health, dashboard, static assets, driver listing, live note creation, conversation mutation, and combined driver context. Empty-data dashboard response was about 0.03 seconds on the validation host.
- Background intake/identity handoff completed with `maintenance_status=ready`; health reported `engine=LMDB`, `online=true`, and zero dirty entries.
- Restart recovery verified SQLite had 0 rows before close, then 1 row after LMDB recovery, with revision 1 and dirty count 0.

- `tests/Run-Tests.ps1`: **86/86 assertions passed**; the latest Linux validation used temporary PowerShell 7.4.6 and SQLite 3.45.1 runtimes. Coverage includes assignment and reassignment history, grouped per-driver Daily Review summaries, unchanged-repair skipping, background startup wiring, maintenance-completion cache refresh, identity-noise suppression, exact duplicate collapsing, preservation of distinct rapid card edits, bulk cleanup preservation, the review index/route, core-owned call schema, persistent cycle completion, automatic pending reset on a new PTA, legacy cycle-key migration, queue/card source wiring, and save-safe progression in addition to coached-share logic, weighted idle safeguards, historical backfill, compact mutations, reminders, timers, transitions, and on-time controls.
- `tests/Identity.Tests.ps1`: **18/18 assertions passed**, including cross-truck `JONESI`/`JONESIRA` family reconciliation, Rolling-first, PTA-first, placeholder reconciliation, shared-unit refusal, assignment history, ambiguous derived-code isolation, and suppression of manufactured identity activity.
- Live startup E2E: the loopback listener reported ready inside the initial one-second launch window; background health reached `ready`; the one-time repair marker persisted; the next unchanged launch reported that reports and identity links were current; and the SQLite startup backup remained active.
- `tests/Measure-PtaPerformance.ps1`: **500 rows**, 104.2 ms parse, 21.3 ms database phase, 2278.8 ms complete preview/reparse/import/identity pipeline, result `responsive` in the final validation environment.
- End-to-end real TCP server exercises passed health, PTA commit, pending driver read, canonical driver-context cycle, compact conversation update, call completion, completed queue state, live duplicate collapse, and bulk Daily Review cleanup, in addition to the prior static delivery, Unicode PTA, organizer, deletion, activity, assignment, and ownership exercises.
- JavaScript syntax, whitespace validation, SQLite schema execution, single-query Driver Card JSON, organizer SQL, daily activity SQL, latest-row tie handling, and contiguous weighted 28-day SQL all passed. The official SQLite Windows archive hash was verified as `88b4659fe747896b853af10157316b4ade143553efb89c1c8ca7423a278dcc8b`.

## Intentionally unresolved business rules

The meanings of PTA `source_numeric_1` and `source_numeric_2` are unknown and are preserved without labels or invented semantics. Missing BOL disappearance does not imply resolution. Home-time state remains manual. Preplan source status remains distinct from reviewed/accepted/denied workflow state. Incomplete rolling coverage is not represented as a confident 28-day result.

## Launch

On the target Windows machine, double-click `Start-Waa.cmd` (or run `Start-Waa.ps1`). WAA selects loopback port 8765–8775, opens the default browser, and needs no installation, elevation, network, Python, Node, Java, package manager, or external PowerShell module.

## Codex Run Metadata

- Integrated revision completed: 2026-08-09T19:30:00Z
- Validation status: complete
