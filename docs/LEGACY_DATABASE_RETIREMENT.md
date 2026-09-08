# Previous-Generation WAA Database Retirement

WAA v0.4.8 introduces a one-time retirement path for the known pre-current WAA data store that used `drivers(id, full_name, pta_code)` plus the older PTA/call/note/reminder/timer/transition/Missing-BOL model.

This is intentionally an **archive-and-restart** operation, not a semantic migration into current work.

## Why it is not merged into current work

The previous generation stored domains the current WAA deliberately no longer models: PTA planning, call sessions, notes/reminders/timers, transition drafts, historical Missing BOL workflow state, and other browser-era operational fields. Converting those rows into current `work_entries` would require inventing statuses, ownership, completion meaning, and current Driver Code mappings.

WAA therefore preserves that data exactly instead of guessing.

## Detection

Automatic retirement occurs only when all of these are true:

- `%LOCALAPPDATA%\WAA\waa.db` exists
- a `drivers` table exists
- that table does **not** contain current durable `driver_code`
- it contains the known previous-generation columns `id`, `full_name`, and `pta_code`
- the previous-generation `schema_version` table exists

A different unknown incompatible schema is never auto-retired. Startup stops and leaves it untouched.

## Archive

Before the original database is removed, WAA:

1. opens the previous-generation database through SQLite
2. creates a consistent SQLite online backup
3. requires `PRAGMA quick_check` on that backup to return `ok`
4. records table row counts in `RETIREMENT-MANIFEST.json`
5. copies the old `%LOCALAPPDATA%\WAA\live` LMDB directory when present so uncheckpointed old live state is preserved too
6. finalizes the archive under `%LOCALAPPDATA%\WAA-Legacy\WAA-retired-<UTC timestamp>`
7. only then removes the old `waa.db` / WAL / SHM files and old `live` directory

If archiving fails before source replacement, the source database is not modified.

## Current WAA after retirement

WAA then initializes its normal current-generation database from scratch. The next normal launch report scan rebuilds the current roster from supported reports in Downloads.

Only compatible presentation preferences are carried forward when recognizable:

- Light/Dark (`appearance_theme`)
- Ambient Motion (`appearance_ambient_motion`)

Old PTA, call-session, note, reminder, timer, transition, Missing BOL, identity, and audit rows remain in the archive. They are not silently converted into current work.

## User visibility

On the one launch where retirement occurs, WAA displays the archive location after the main window opens. The operation is also logged.
