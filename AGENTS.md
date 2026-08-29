# WAA fresh-build rules

This repository is a clean rebuild.

## Authority

- Use the current README and `docs/` planning files as project authority.
- `docs/IDLE_WORKFLOW.md` is authoritative for weighted idle, priority, and conversation-cycle behavior.
- Do **not** inspect, copy, port, or resurrect implementation/design/schema ideas from repository history unless the user explicitly asks for a specific historical item.
- Uploaded/current operational reports may be used as source contracts.

## Core model invariants

- Driver Code is the durable driver key; Driver Name is its human-readable identity.
- Unit Code is an object/assignment observation, never driver identity.
- Driver Leader is organizational context, never driver identity.
- Never silently guess, fuzzy-merge, or resolve conflicting identity/source rows.
- Preserve historical drivers, observations, work, and conversation events when roster context changes.

## Report-update invariants

- Scan/import reports automatically once during launch.
- After launch, import only through the explicit `Update Reports` action.
- Do not add `FileSystemWatcher`, periodic polling, recurring directory scans, or automatic mid-session imports.
- Preserve the last known-good roster when an import fails.
- Imported files are read-only inputs and are never modified or deleted.
- Hash accepted source content and make imports idempotent and atomic.

## Weighted-idle invariants

- Weighted 7-day idle = raw 7-day idle hours / raw 7-day engine hours.
- Weighted 28-day idle = summed idle hours / summed engine hours across the current period and three expected prior weekly periods.
- Never calculate 28-day idle by averaging weekly percentages.
- Require all four expected observations for a complete driver 28-day value; expose incomplete coverage.
- Fleet weighted values are also numerator/denominator calculations and must expose coverage.
- Default threshold is 50%, locally configurable, with a strict greater-than comparison.
- Either valid 7-day or complete 28-day idle above threshold puts a driver in the high-idle population.

## Idle-conversation invariants

- Conversation state is keyed by Driver Code + Report Cycle Date.
- Outcomes distinguish `Attempted`, `Spoke`, and `Spoke — Follow-up`; no event means `Not Contacted`.
- Same-cycle corrected reports preserve conversation state.
- A newer cycle derives fresh pending state without deleting prior history.
- Above-threshold unfinished conversations sort first, above-threshold completed conversations next, then the remaining fleet.
- The user must never need to maintain a weekly “already contacted” filter or reset checkboxes.
- Idle actions snapshot metrics, threshold, Unit Code, Driver Leader, source import, and timestamp.

## Product and performance discipline

- Optimize for a smooth work-through and accurate end-of-shift handoff.
- Keep the main workflow on one restrained, professional screen.
- Target low-spec Windows hardware using native WPF controls, virtualized rows, indexed queries, and import-time calculations.
- Show the last known-good roster immediately and run launch import off the UI thread.
- No browser shell, WebView, local HTTP server, continuous animation, blur, glow, decorative charts, gamification, or oversized KPI cards.
- Add a feature only when it reduces work, prevents missed follow-up, improves idle accountability, or improves handoff accuracy.

## Current build order

1. Rolling 7 Day ingestion, roster identity, weighted driver/fleet metrics, threshold, and prioritized virtualized fleet list.
2. Per-cycle idle conversation tracking and automatic rollover/ordering.
3. General driver work card and Done / Waiting / Follow-up entries.
4. Editable handoff generation.
5. Missing BOL integration.
6. Evaluate maintenance and DOT separately.

Do not jump ahead while an earlier phase's exit criteria are unverified.
