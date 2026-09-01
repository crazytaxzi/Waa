# WAA Implementation Status

## Current bounded release

**WAA v0.4.5.1 — Ambient Motion Control Hotfix, merged to `main` and Windows-validated.**

This release is a presentation/settings hotfix only. It restores direct user control of the v0.4.5 ambient-motion layer on Windows sessions where `SystemParameters.ClientAreaAnimation` reports false. It does not change database schema/version, report parsing, queue priority, durable identity, work/BOL/Handoff behavior, route identity, or `%LOCALAPPDATA%\WAA` compatibility.

Merged product commit: `3e1282cb9f2a6360a2a379773fab4ff9891a781a`.

Merged-main Windows validation: **Windows build, test, and portable package #84**, run ID `33540852195`, September 1, 2026 — **success**.

## v0.4.5 ambient shell

Dark mode keeps the deliberately faint decorative layer:

- one slow rolling scanline
- eight sparse 2–3 pixel electric-blue motes
- centralized `AmbientScanlineBrush` and `AmbientParticleBrush` palette roles
- no glow, blur, shader, shadow, particle engine, timer, background worker, browser surface, or new dependency
- overlay is clipped and `IsHitTestVisible=False`

The ambient Storyboard runs only when Dark mode is active and the current WAA motion state is enabled. Light mode always suppresses it.

## v0.4.5.1 motion-control hotfix

The v0.4.5 defect was that Windows `SystemParameters.ClientAreaAnimation == false` forced the shell into a disabled `Motion reduced` state, preventing the user from explicitly choosing WAA motion on or off.

v0.4.5.1 changes that behavior:

- Windows client-animation state is used only to seed the initial runtime default when no WAA motion preference has ever been saved.
- If Windows client animation is disabled and no WAA preference exists, WAA starts with ambient motion off and an enabled `Motion on` button.
- If Windows client animation is enabled and no WAA preference exists, WAA starts with ambient motion on and an enabled `Motion off` button.
- The WAA Motion button never becomes permanently disabled because of Windows/RDP/enterprise animation state.
- The first explicit user click stores `appearance_ambient_motion=on|off` in the existing `settings` table.
- Once saved, that explicit WAA preference is authoritative on later launches even if Windows reports client-area animation disabled.
- Preference saving still runs off the UI thread and restores the previous visible state if persistence fails.

No schema migration or schema version change is required.

## Button feedback

Central `BaseButtonStyle` remains unchanged from v0.4.5:

- hover scale maximum `1.012x`
- short 0.12/0.14 second enter/leave transitions
- slight pressed opacity change

This does not change layout, click targets, commands, focus behavior, semantic colors, or keyboard accessibility.

## Preserved v0.4.x behavior

Still preserved:

- denser virtualized Fleet Queue with full-row click and Enter-to-open
- native Up/Down DataGrid navigation
- exact-code Missing BOL matching and unmatched preservation
- Driver Leader-grouped compact Handoff
- one-window Fleet → Driver → Task routing and state restoration
- theme-safe text, contrast validation, and complete dark shell background
- report updates only at launch/manual Update Reports
- `%LOCALAPPDATA%\WAA` data and preference compatibility
- self-contained Windows x64 portable deployment

## Validation

PR #8 hotfix tree, workflow **#83**, run ID `33540678569`: success.

Merged product `main`, workflow **#84**, run ID `33540852195`:

- restore: passed
- warnings-as-errors Release build: passed
- WPF/XAML compilation: passed
- Core tests: **24 passed**
- App/SQLite/navigation/theme/Handoff/ambient-motion/integration tests: **198 passed**
- total: **222 passed, 0 failed, 0 skipped**
- build: **0 warnings, 0 errors**
- self-contained win-x64 publish: passed
- portable artifact upload: passed
- merged-product artifact SHA-256: `431cf80a0522229c9c73f6f90f7f07a241a9dc79a07f886d331ea96a2a4d62d0`

Regression coverage now explicitly prevents the disabled `Motion reduced` path from returning and verifies that Windows client-animation state is only an unsaved initial-default signal.

## Database compatibility

v0.4.5.1 requires **no database schema change**. Existing `%LOCALAPPDATA%\WAA` preserves roster/import metadata, idle contacts, work history, Missing BOL state/actions, threshold, Light/Dark preference, Handoff state, and any previously saved Ambient Motion preference.

## Remaining limitations

- ACE/ACI state is not stored/validated; generated opening remains an editable convention
- coached/not-coached state is not stored and is not invented in Handoff
- unmatched BOL cannot become driver-owned until exact durable Driver Code exists
- no manual/fuzzy BOL assignment
- no emailing/transmitting BOL/documents
- no automatic calls/messages/contact
- no OCR/image recognition/document storage/uploads/attachments
- no BOL analytics/revenue dashboard
- no escalation/routing/approval engine
- no heavy animation/glow/blur/particle engine beyond the bounded v0.4.5 layer
- Maintenance and DOT remain separate unimplemented evaluations
- no destructive work-entry deletion
- no full corrective idle-event editing/audit UI
- no dedicated Driver Leader filter
- no measured representative low-end office-PC benchmark outside GitHub-hosted validation