WAA PORTABLE — FIRST RUN AND UPGRADE
====================================

FIRST INSTALL
-------------
1. Extract the complete WAA-Portable-win-x64 archive to a normal local folder.
   Do not run WAA from inside the ZIP and avoid running it directly from a network drive.

2. Put available current reports in your Windows Downloads folder.

   Rolling roster/idle source:
   rolling 7 day_data*.csv

   Missing BOL source:
   Order Details Missing BOL*.xlsx

   Temporary Office lock files beginning with ~$ are ignored.

3. Double-click WAA.exe.

WAA needs no installer, administrator access, SDK, separately installed .NET runtime, Excel,
or Office. It checks Downloads once during launch. Reports added later are imported only when
you click Update Reports.

UPGRADE
-------
1. Close WAA.
2. Extract the new portable folder.
3. Replace the old application folder with the new one.
4. Keep the saved data folder in place:
   %LOCALAPPDATA%\WAA
5. Start WAA.exe.

Replacing or deleting the portable application folder does not automatically delete the saved
roster, settings, idle contacts, Missing BOL state, work history, or handoff source records.
Database upgrades are applied non-destructively at startup. A migration error is shown and
logged instead of replacing the existing database.

MISSING BOL v0.3
----------------
- weighted driver and fleet 7-day idle
- weighted driver and fleet 28-day idle with coverage
- configurable idle threshold
- automatic high-idle and unresolved-work priority ordering
- per-cycle Spoke, Attempted, and Spoke — Follow-up tracking
- managed read-only Missing BOL XLSX import without Excel
- exact Driver Code matching only; no fuzzy/name/unit/truck/leader guessing
- compact BOL count, Order # search, and unmatched source-code list
- selected-driver Missing BOL orders with Requested, Attempted, Follow-up, Resolved, and Reopen
- one linked open task per matched unresolved Missing BOL item
- disappearance from a later workbook never resolves local work
- manual Done, Waiting, and Follow-up work
- unresolved work carried forward until resolved
- idle contacts and Missing BOL actions recorded in unified work history
- Open Work and Today’s Activity for the selected driver
- Next Needing Attention that respects search results
- editable deterministic Handoff
- Copy to Clipboard
- saved light and dark appearance
- launch-only automatic report update plus manual Update Reports
- independent Rolling and Missing BOL outcomes with last-known-good preservation

Missing BOL is deliberately a local work queue. WAA does not email or transmit documents,
contact drivers automatically, perform OCR, store attachments, provide BOL analytics, or use
fuzzy matching or complex escalation logic.

If company security blocks WAA.exe, do not bypass company policy. Record the exact message so
the packaging can be adjusted or reviewed by the appropriate support staff.
