WAA PORTABLE — FIRST RUN AND UPGRADE
====================================

FIRST INSTALL
-------------
1. Extract the complete WAA-Portable-win-x64 archive to a normal local folder.
   Do not run WAA from inside the ZIP and avoid running it directly from a network drive.

2. Put the current Rolling 7 Day report in your Windows Downloads folder.
   Expected filename family: rolling 7 day_data*.csv

3. Double-click WAA.exe.

WAA needs no installer, administrator access, SDK, or separately installed .NET runtime.
It checks Downloads once during launch. Reports added later are imported only when you click
Update Reports.

UPGRADE
-------
1. Close WAA.
2. Extract the new portable folder.
3. Replace the old application folder with the new one.
4. Start WAA.exe.

Saved data and appearance preferences remain separately under:
%LOCALAPPDATA%\WAA

Replacing or deleting the portable application folder does not automatically delete the saved
roster, settings, idle contacts, work history, or handoff source records. Database upgrades are
applied non-destructively at startup. A migration error is shown and logged instead of replacing
the existing database.

WORK LOG + HANDOFF v0.2
-----------------------
- weighted driver and fleet 7-day idle
- weighted driver and fleet 28-day idle with coverage
- configurable idle threshold
- automatic high-idle and unresolved-work priority ordering
- per-cycle Spoke, Attempted, and Spoke — Follow-up tracking
- manual Done, Waiting, and Follow-up work
- unresolved work carried forward until resolved
- idle contacts automatically recorded in unified work history
- Open Work and Today’s Activity for the selected driver
- Next Needing Attention that respects search results
- editable deterministic Handoff
- Copy to Clipboard
- saved light and dark appearance
- launch-only automatic report update plus manual Update Reports

If company security blocks WAA.exe, do not bypass company policy. Record the exact message so the
packaging can be adjusted or reviewed by the appropriate support staff.
