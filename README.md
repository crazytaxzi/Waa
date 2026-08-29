# WAA — Work Activity & Handoff

WAA is being rebuilt from scratch as a small, professional work log for one purpose: **keep track of what was handled during the shift and make end-of-day handoff easy.**

## Product direction

WAA should look and behave like a normal workplace tool, not a dashboard, game, or command center.

### Core flow

1. **Capture** — quickly record what happened.
2. **Track** — see what is done, waiting, or still needs attention.
3. **Handoff** — generate a concise end-of-shift summary of completed work and unresolved items.

That flow should stay obvious from the moment the app opens.

## Design rules

- Professional and low-key enough to leave open on a work computer.
- Dense, readable, and fast rather than decorative.
- Neutral workplace styling; no neon, glow, animated backgrounds, oversized metrics, gamification, or visual noise.
- Use ordinary system fonts and familiar controls.
- One main screen should handle most of the shift.
- Important information is shown by hierarchy, wording, and spacing before color.
- Color is reserved for status and exceptions.
- Every feature must directly support capture, follow-up, or handoff.
- No feature exists merely because the previous version had it.

## Initial information model

A work entry should stay simple:

- Time
- Subject / driver / unit / task
- What happened
- Status: `Done`, `Waiting`, or `Needs Follow-up`
- Optional follow-up note

The exact fields can evolve only after the basic workflow proves useful.

## Handoff output

The handoff should favor short, useful operational language and separate:

- Completed today
- Still waiting / pending
- Needs follow-up next shift
- Important notes

The user must be able to edit the handoff before copying it.

## Scope discipline

The old WAA implementation is intentionally retired. Previous dashboards, coaching views, chart systems, multi-database architecture, report automation, and specialty workflows are **not requirements** for this rebuild.

If an old capability is genuinely needed later, it should be reintroduced only after answering two questions:

1. Does it reduce work or prevent something from being missed?
2. Can it fit the simple shift-log-to-handoff flow without making the tool look busy or recreational?

If either answer is no, leave it out.

## Current state

Clean restart. No application implementation has been chosen yet.
