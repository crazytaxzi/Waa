# WAA — Driver-Centric Work & Handoff

WAA is a fresh, local Windows work tool for keeping track of driver-related work during a shift and producing a clean handoff at the end of the day.

**This rebuild does not use the old WAA implementation or repository history as a design source.**

## Core model

The **driver is the primary entity**.

- **Driver Code** is the durable identity key.
- **Driver Name** is the human-readable identity.
- **Unit Code / truck** is an object used by the driver and may change over time. A truck must never become driver identity.
- **Driver Leader** is current organizational context and may also change over time.

The current driver roster is derived from the newest valid `rolling 7 day_data*.csv` export in the Windows Downloads folder.

## Primary workflow

WAA should support one obvious work-through flow:

1. **Find or select a driver.**
2. **See the driver context immediately** — code, name, current unit, Driver Leader.
3. **Record what happened** with as little typing and clicking as practical.
4. **Mark the item** as Done, Waiting, or Follow-up.
5. **Move to the next driver or task.**
6. **Generate the handoff** from the work recorded during the shift plus unresolved carry-forward items.

There is no dashboard-first experience. The work list is the home screen.

## UI direction

The app must look like a normal professional workplace utility.

- light, neutral Windows-style interface
- system typography
- compact rows and controls
- clear spacing and hierarchy
- status color only where it communicates meaning
- no glow, gradients for decoration, animated backgrounds, oversized KPI tiles, game-like widgets, or ornamental charts
- no hidden multi-step maze for common actions

## Technical direction

Initial implementation target:

- **.NET 10 LTS**
- **WPF** desktop UI
- **SQLite** local persistence via `Microsoft.Data.Sqlite`
- self-contained Windows x64 publishing so the app does not depend on a separately installed .NET runtime
- no network service required for normal operation

The app watches the Windows Downloads folder for completed Rolling 7 Day CSV exports, but file-system events are treated only as a signal to rescan. The newest valid file is selected, validated, hashed, and imported atomically. A failed or partial import never destroys the last good roster.

## Planning documents

- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — product, UX, architecture, phases, and acceptance criteria
- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — fresh source contracts for the supplied operational reports

## First implementation milestone

Build only the roster foundation first:

- locate Downloads correctly
- discover the newest `rolling 7 day_data*.csv`
- validate required columns
- parse Driver Code + Driver Name from the export's driver label
- capture Driver Leader and Unit Code
- deduplicate the report's measure rows
- persist drivers and unit observations
- display a fast searchable driver list
- update automatically when a newer valid export finishes downloading

Do not add Missing BOL, maintenance, DOT, charts, coaching, analytics, or other specialty workflows until this foundation is correct and pleasant to use.
