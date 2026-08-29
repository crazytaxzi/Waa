WAA PORTABLE — FIRST RUN
========================

1. Extract the complete WAA-Portable-win-x64 archive to a normal local folder.
   Do not run WAA from inside the ZIP and avoid running it directly from a network drive.

2. Put the current Rolling 7 Day report in your Windows Downloads folder.
   Expected filename family: rolling 7 day_data*.csv

3. Double-click WAA.exe.

WAA needs no installer, administrator access, SDK, or separately installed .NET runtime.
It checks Downloads once during launch. Reports added later are imported only when you click
Update Reports.

Saved data is kept separately under:
%LOCALAPPDATA%\WAA

That means replacing or deleting this portable application folder does not automatically delete
your saved roster and idle-contact history.

This first test build includes:
- weighted driver and fleet 7-day idle
- weighted driver and fleet 28-day idle with coverage
- configurable idle threshold
- automatic high-idle priority ordering
- per-cycle Spoke, Attempted, and Spoke — Follow-up tracking
- launch-only automatic report update
- manual Update Reports

If company security blocks WAA.exe, do not bypass company policy. Record the exact message so the
packaging can be adjusted or reviewed by the appropriate support staff.
