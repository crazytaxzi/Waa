# WAA Implementation Status

## Current bounded release

**WAA v0.4.5 — Ambient Motion Theme Layer, PR-tree Windows-validated.**

This release is presentation-only. It adds bounded ambient shell motion, a persisted Motion setting, and restrained button feedback. It does not change database schema/version, report parsing, queue priority, durable identity, work/BOL/Handoff behavior, route identity, or `%LOCALAPPDATA%\WAA` compatibility.

Validated release branch head before this status commit: `472fd8fbb6aa318c6e1761403c7ecb81ae77dcfe`.

PR #7 Windows validation: **Windows build, test, and portable package #76**, run ID `33536942344`, September 1, 2026 — **success**.

## v0.4.5 ambient shell

Dark mode now has a deliberately faint decorative layer:

- one slow rolling scanline
- eight sparse 2–3 pixel electric-blue motes
- centralized `AmbientScanlineBrush` and `AmbientParticleBrush` palette roles
- no glow, blur, shader, shadow, particle engine, timer, background worker, browser surface, or new dependency
- overlay is clipped and `IsHitTestVisible=False`

The ambient layer runs only when the persisted user preference is enabled, Dark mode is active, and Windows `SystemParameters.ClientAreaAnimation` permits client animation. Light mode always suppresses it.

## Motion preference

The shell exposes a compact motion control:

- `Motion off` means ambient motion is currently enabled and can be switched off
- `Motion on` means the preference is disabled and can be enabled
- `Motion reduced` appears disabled when Windows client animations are disabled

The new preference uses the existing SQLite `settings` table key `appearance_ambient_motion`. Missing preference defaults to enabled. Saving runs off the UI thread and rolls visible state back on persistence failure. No schema migration or schema version change is required.

## Button feedback

Central `BaseButtonStyle` adds only template-local render feedback:

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

## Branch validation

PR #7 release tree, workflow **#76**, run ID `33536942344`:

- restore: passed
- warnings-as-errors Release build: passed
- WPF/XAML compilation: passed
- Core tests: **24 passed**
- App/SQLite/navigation/theme/Handoff/ambient-motion/integration tests: **198 passed**
- total: **222 passed, 0 failed, 0 skipped**
- build: **0 warnings, 0 errors**
- self-contained win-x64 publish: passed
- portable artifact upload: passed
- branch artifact SHA-256: `622231f126a0c45c5990e4ff99484098c82de30772c9617801a7d91e97970c3e`

An earlier PR run correctly caught an old shell-theme regression assertion that depended on the pre-overlay XAML indentation/layout. The test was changed to validate dynamic shell-background usage semantically; production dark-shell behavior was not weakened. Button motion was also tightened to a template-local transform before final branch validation.

## Final validation gate

This documentation-aligned PR head must pass the same full Windows workflow. PR #7 must then merge normally to `main`, and the exact merged-main commit must pass restore/build/test/publish/artifact upload again. Only the validated merged-main portable artifact is delivered.

## Database compatibility

v0.4.5 requires **no database schema change**. Existing `%LOCALAPPDATA%\WAA` preserves roster/import metadata, idle contacts, work history, Missing BOL state/actions, threshold, Light/Dark preference, Handoff state, and the new ambient-motion preference.

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
