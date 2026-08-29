# WAA — Driver-Centric Work & Handoff

WAA is a fresh, local Windows work tool for working through a driver fleet, recording what was handled during the shift, and producing a clean handoff at the end of the day.

**This rebuild does not use the old WAA implementation or repository history as a design source.**

## Core model

The **driver is the primary entity**.

- **Driver Code** is the durable identity key.
- **Driver Name** is the human-readable identity attached to that code.
- **Unit Code / truck** is an object used by the driver and may change over time. A truck must never become driver identity.
- **Driver Leader** is current organizational context and may also change over time.

The current driver roster and idle observations come from the newest valid `rolling 7 day_data*.csv` export in the current user's Windows Downloads folder.

## Report refresh behavior

WAA updates reports in exactly two ways:

1. **Automatically once during application launch.**
2. **Manually when the user chooses `Update Reports`.**

There is no continuous folder watcher, polling loop, or automatic mid-shift import. Dropping a newer or corrected report into Downloads does nothing until `Update Reports` is selected or WAA is launched again.

Each update rescans Downloads, validates the newest matching report, hashes it, and imports it atomically. An invalid, incomplete, or locked file never replaces the last known-good roster.

## Weighted idle rules

All displayed idle percentages are weighted from the report's raw engine and idle hours. WAA must never calculate a 28-day result by averaging weekly percentages.

- **Driver weighted 7-day idle %** = newest valid week's idle hours / engine hours × 100.
- **Driver weighted 28-day idle %** = sum of idle hours across the four expected weekly periods / sum of engine hours across those same periods × 100.
- **Fleet weighted 7-day idle %** = total newest-week idle hours for the active fleet / total newest-week engine hours × 100.
- **Fleet weighted 28-day idle %** = total eligible four-week idle hours / total eligible four-week engine hours × 100, with coverage shown.

A zero denominator displays `N/A`. A driver missing one of the four expected weekly periods displays incomplete 28-day coverage rather than a falsely confident percentage.

The idle attention threshold defaults to **50%**, is locally configurable, and uses a strict `>` comparison. Changing it re-ranks the current list immediately without rewriting historical records.

## Primary workflow

The main fleet list is both the work queue and the visual idle overview. Every driver row shows:

- Driver Code
- Driver Name
- Unit Code
- Driver Leader
- weighted 28-day idle %
- weighted 7-day idle %
- current idle-conversation status
- unresolved work status when applicable

The default order requires no weekly filter maintenance:

1. Above-threshold drivers who still need an idle conversation for the current reporting cycle.
2. Above-threshold drivers already spoken to for the current reporting cycle.
3. All remaining drivers.

Within the priority groups, the highest current idle concern sorts first. The user may still search or sort, but the default view must remain useful without touching a filter.

Selecting a driver opens one restrained work card where the user can:

1. See current driver, unit, leader, 28-day idle, and 7-day idle context.
2. Record ordinary work as Done, Waiting, or Follow-up.
3. Record an idle outcome as `Spoke`, `Attempted`, or `Spoke — Follow-up` with an optional note.
4. Move directly to the next driver needing attention.

Idle conversation records are tied to the report's current weekly cycle. Importing a newer weekly cycle automatically creates a new need-to-contact state for drivers above the threshold while preserving prior conversation history. Marking a driver `Spoke` updates the list immediately and moves that driver below still-uncontacted priority drivers—no manual “already talked to” filter and no weekly reset button.

## UI and low-end PC requirements

WAA must look like a normal professional workplace utility and run well on a low-spec Windows PC.

- native WPF controls; no browser shell or WebView dependency
- light, neutral Windows-style interface using system typography
- compact virtualized rows and controls
- clear spacing and hierarchy
- two aligned numeric idle columns visible for the whole fleet
- restrained text/status emphasis when a value exceeds the configured threshold
- no glow, decorative gradients, backdrop blur, animated backgrounds, oversized KPI tiles, gamification, or ornamental charts
- no continuous animation, report polling, or background folder watcher
- open the last known-good roster immediately, then complete the one launch update off the UI thread
- calculate and persist idle snapshots at import time so scrolling and sorting do not recalculate report history per row

The application should remain calm and obviously work-related when viewed by a coworker or supervisor.

## Technical direction

Initial implementation target:

- **.NET 10 LTS**
- **WPF** desktop UI
- **SQLite** local persistence via `Microsoft.Data.Sqlite`
- self-contained Windows x64 publishing
- one local desktop process and no network service
- row virtualization and indexed roster/priority queries

## Planning documents

- [`docs/PROJECT_PLAN.md`](docs/PROJECT_PLAN.md) — product, UX, architecture, phases, and acceptance criteria
- [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md) — fresh source contracts and weighted idle calculations
- [`docs/IDLE_WORKFLOW.md`](docs/IDLE_WORKFLOW.md) — idle priority, weekly conversation state, and rollover rules

## First implementation milestone

Build the roster and weighted-idle foundation first:

- locate Downloads correctly
- update once at launch plus an explicit `Update Reports` action
- discover and validate the newest `rolling 7 day_data*.csv`
- parse Driver Code + Driver Name from the real export format
- capture Driver Leader and Unit Code
- normalize repeated measure rows into one driver/week observation
- calculate weighted 7-day and complete-coverage weighted 28-day percentages
- calculate compact fleet weighted 7-day and 28-day summaries
- persist drivers, weekly observations, imports, and the configurable threshold
- display a fast searchable and automatically prioritized fleet list

The next milestone adds competent per-cycle idle conversation tracking before broader report integrations are considered.
