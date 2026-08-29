# WAA fresh-build rules

This repository is a clean rebuild.

## Authority

- Use the current README and `docs/` planning files as project authority.
- Do **not** inspect, copy, port, or resurrect implementation/design/schema ideas from repository history unless the user explicitly asks for a specific historical item.
- Uploaded/current operational reports may be used as source contracts.

## Core invariants

- Driver Code + Driver Name define the driver entity; Driver Code is the durable key.
- Unit Code is an object/assignment observation, never driver identity.
- Driver Leader is organizational context, never driver identity.
- Never silently guess or fuzzy-merge driver identity.
- Preserve last known-good roster when an import fails.
- Imported files are read-only inputs.

## Product discipline

- Optimize for a smooth work-through and end-of-shift handoff.
- Keep the main workflow on one restrained professional screen.
- No dashboard-first design, neon/glow, continuous animation, decorative charts, gamification, or oversized KPI cards.
- Add a feature only when it reduces work, prevents missed follow-up, or improves handoff accuracy.

## Current build order

1. Rolling 7 Day roster ingestion and automatic refresh.
2. Driver work card and Done / Waiting / Follow-up entries.
3. Editable handoff generation.
4. Missing BOL integration.
5. Evaluate maintenance and DOT separately.

Do not jump ahead while an earlier phase's exit criteria are unverified.
