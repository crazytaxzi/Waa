# WAA Implementation Status

## Current bounded release

**WAA v0.4.6 — Source-Only Missing BOL, release tree Windows-validated.**

v0.4.6 deliberately simplifies Missing BOL: the current accepted `Order Details Missing BOL*.xlsx` workbook is the source of truth. Current BOL rows live only in memory and are displayed read-only. WAA no longer persists current BOL imports/items/status/actions/notes/history or generates current BOL work tasks.

Validated release-tree head before this status-only commit: `8deaf9f82ffa49b9bbb40d49aebf2adef3d00433`.

PR #9 Windows validation: **Windows build, test, and portable package #110**, run ID `33553531840`, September 1, 2026 — **success**.

## v0.4.6 source-only Missing BOL

Current behavior:

- accepted workbook rows are held only in memory for the current session
- same-session SHA-256 detects unchanged workbook bytes; BOL hashes are not persisted
- exact normalized `Last Dispatch Driver cd` matches only exact **current** durable Driver Code
- source Driver Name remains evidence only; exact-code name mismatch warns without changing roster identity
- unmatched current workbook rows remain visible/read-only
- current matched rows appear in a dedicated `CURRENT MISSING BOL` Driver Workspace section
- focused BOL order detail is read-only
- no Requested / Attempted / Follow-up / Resolved / Reopen BOL controls
- no BOL note editor or BOL action history
- current BOL rows do not increase Open Work, `NEEDS ATTENTION`, queue priority, `Next Work Item`, or Today’s Activity
- Fleet search still accepts current BOL Order # text and Fleet/Driver presentation still shows current report counts
- shell summary says `Missing BOL file: N matched • M unmatched`
- Handoff `Missing BOLs:` is generated transiently from current matched workbook rows when Handoff is regenerated
- no matching workbook clears the current in-memory BOL view after the scan
- restarting WAA does not restore BOL rows from SQLite; the workbook is scanned again

## Legacy database compatibility

Older v0.3–v0.4.5 installations may already contain `missing_bol_*` tables and generated `MissingBolTask` / `MissingBolAction` work rows.

v0.4.6 is intentionally non-destructive:

- it does not drop/rewrite those legacy tables/history during normal upgrade
- legacy BOL state never repopulates the current workbook view
- legacy generated BOL work is classified and excluded from current Open Work, Today’s Activity, queue priority, and current ordinary Handoff narrative
- unresolved legacy BOL task counts are subtracted from current Open Work presentation
- fresh v0.4.6 databases do not create Missing BOL tables

Rolling 7 Day, idle-accountability, manual work, settings, appearance, and other durable SQLite state remain unchanged.

## Driver Workspace and navigation

`NEEDS ATTENTION` now represents actual actionable idle/manual work only.

`CURRENT MISSING BOL` is a separate read-only report section. Its rows remain keyboard/mouse accessible and may open the same-window read-only order detail, but they are not work items.

`Next Work Item` explicitly skips current BOL report rows. A regression test walks Idle → actual manual Follow-up while a current BOL row is present and verifies the BOL row is not inserted into the work sequence.

## Handoff

Handoff still begins with the editable convention `No open ACE/ACI's` and retains Driver Leader-grouped compact saved-work narrative.

The dedicated `Missing BOLs:` section now comes from the current in-memory workbook only. It uses current exact Driver Code ownership/current fleet context and groups current Order # values per represented driver. No BOL database write is performed merely to produce Handoff.

Legacy BOL-generated work is filtered out before current Handoff generation.

## Preserved v0.4.x behavior

Still preserved:

- one native WPF `MainWindow` central workspace
- denser virtualized Fleet Queue with full-row click/Enter and native Up/Down
- durable Driver Code identity
- weighted 7-day/complete-coverage 28-day idle calculations
- current-cycle idle accountability with atomic linked work
- manual Done / Waiting / Follow-up work and Resolve/Reopen
- unresolved saved work carry-forward
- Driver Leader-grouped compact Handoff
- explicit launch/manual report-update cadence; no watcher/polling
- theme-safe text and complete dark shell
- v0.4.5 faint scanline/eight sparse electric-blue motes/button feedback
- v0.4.5.1 user-authoritative Ambient Motion toggle
- `%LOCALAPPDATA%\WAA` upgrade compatibility
- self-contained Windows x64 portable deployment

## Release-tree validation

PR #9 release tree, workflow **#110**, run ID `33553531840`:

- restore: passed
- Release/WPF build: passed
- Core tests: **24 passed**
- App/SQLite/navigation/theme/Handoff/source-only-BOL/integration tests: **187 passed**
- total: **211 passed, 0 failed, 0 skipped**
- build: **0 warnings, 0 errors**
- self-contained win-x64 publish: passed
- portable artifact upload: passed
- release-tree artifact SHA-256: `b6e0da389216994799164517d817298165d811483356883a46ca98968c38501f`

The suite is smaller than v0.4.5.1 because obsolete tests whose purpose was to require persisted BOL task/action/restart behavior were removed and replaced by source-only tests covering in-memory replacement, restart emptiness, exact current-roster matching, no BOL schema/work creation, legacy-work exclusion, read-only current-file detail, current Order # search, BOL-independent queue/work sequencing, and transient current-file Handoff.

This status-only branch commit must pass the same full Windows workflow before PR #9 is marked ready and merged. The exact merged-main product commit must then pass the same gate before final delivery.

## Database compatibility

v0.4.6 performs no destructive BOL migration. Retain `%LOCALAPPDATA%\WAA` during upgrade.

Current durable state preserved includes roster/import metadata for Rolling 7 Day, weekly observations, idle contacts/linked work, manual work history, threshold, Light/Dark preference, and Ambient Motion preference. Legacy BOL DB artifacts may remain physically present but are dormant compatibility data rather than current BOL product state.

## Remaining limitations

- ACE/ACI state is not stored/validated; generated opening remains an editable convention
- coached/not-coached state is not stored and is not invented in Handoff
- unmatched BOL remains unmatched until the current roster contains the exact durable Driver Code
- no manual/fuzzy BOL assignment
- no local BOL status/action/note history by design
- no emailing/transmitting BOL/documents
- no automatic calls/messages/contact
- no OCR/image recognition/document storage/uploads/attachments
- no BOL analytics/revenue dashboard
- no escalation/routing/approval engine
- no heavy animation/glow/blur/particle engine beyond the bounded v0.4.5 layer
- Maintenance and DOT remain separate unimplemented evaluations
- no destructive ordinary work-entry deletion
- no full corrective idle-event editing/audit UI
- no dedicated Driver Leader filter
- no measured representative low-end office-PC benchmark outside GitHub-hosted validation