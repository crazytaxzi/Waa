# WAA Theming v0.4.5.1

## Meaning of Auto text

“Auto” text in WAA means ordinary UI text automatically follows the currently selected Light or Dark palette through WPF resource inheritance and `DynamicResource` references. It does **not** mean an OS-synchronized/System appearance mode. WAA keeps the explicit Light/Dark choice and stores that preference locally.

## Resource ownership

Theme color ownership remains centralized:

- `Themes/LightColors.xaml` — literal colors for Light mode
- `Themes/DarkColors.xaml` — literal colors for Dark mode
- `Themes/BaseStyles.xaml` — control styles consuming palette keys through `DynamicResource`
- `App.xaml` — active palette, base styles, and workspace DataTemplates
- `ThemeManager` — replaces only the active palette dictionary

Literal theme colors are not allowed in MainWindow, UserControls, view-models, converters, or task templates. Light and Dark dictionaries must contain matching key sets.

## Stream palette

Dark mode uses gunmetal/neutral surfaces as the foundation:

- app background `#11161B`
- panel background `#1A232C`
- raised/subtle panel `#24303A`
- header chrome `#202A33`

Purple remains the primary selection/focus/breadcrumb/Handoff accent. Green remains the positive/completed/`Next Needing Attention` accent. Ordinary body text remains neutral/light rather than neon. Light mode preserves the same semantic accent roles on light neutral surfaces.

## Complete shell background

`MainWindow`, the outer shell Grid, and the inner client Grid explicitly consume `WindowBackgroundBrush` through `DynamicResource`. This prevents a Windows/default light surface from remaining visible around dark-themed content. The Windows title bar continues to use the existing DWM helper where supported.

## v0.4.5 ambient motion + v0.4.5.1 control hotfix

Ambient motion is an intentionally bounded visual layer, not a general animation framework.

Dark-mode ambient effects consist of:

- one faint rolling scanline using `AmbientScanlineBrush`
- eight fixed sparse 2–3 pixel electric-blue motes using `AmbientParticleBrush`
- very low opacity and slow 11–19 second movement

The ambient overlay is clipped to the MainWindow client area, sits above the shell visually, and is `IsHitTestVisible=False`. It cannot intercept mouse input or alter workspace routing, selection, text editing, or button hit targets.

Ambient motion runs only when both conditions are true:

1. the current WAA Ambient Motion preference is enabled
2. Dark mode is active

Windows `SystemParameters.ClientAreaAnimation` is now an **initial-default signal only**, not a permanent lockout. When no WAA motion preference has ever been saved, WAA starts with motion enabled if Windows client animation is enabled and starts with motion disabled if Windows client animation is disabled. The shell control remains enabled either way.

As soon as the user clicks the WAA motion control, WAA stores an explicit `on` or `off` preference under `appearance_ambient_motion`. That explicit WAA choice is authoritative on later launches, including RDP, enterprise-policy, or performance-tuned Windows sessions where `SystemParameters.ClientAreaAnimation` reports false.

The user control displays `Motion off` while WAA motion is enabled and `Motion on` while disabled. There is no longer a greyed-out `Motion reduced` state. The legacy convenience method still treats an absent setting as enabled for compatibility, while MainWindow uses the nullable preference to choose the Windows-informed first-run default. No schema version or migration is introduced. Preference writes run off the UI thread; a failed save restores the previous visible state.

Ambient motion is intentionally cheap:

- no DispatcherTimer or recurring polling
- no particle engine or per-frame particle allocation
- no background worker
- no blur, glow, shadow, shader, or external graphics dependency
- no animation inside DataGrid rows or editable text controls
- one fixed Storyboard starts/stops with theme and WAA preference state

## Button motion

`BaseButtonStyle` adds restrained template-local render feedback:

- hover scales the template border to only `1.012x`
- leaving hover returns it to `1.0x`
- press uses a slight opacity reduction
- the ScaleTransform belongs to each button template instance rather than a shared style transform

The transform is render-only. It does not change layout measurement, commands, keyboard accessibility, semantic colors, focus boundaries, or click targets. Existing neutral/purple/green hover colors remain authoritative.

## Ordinary foreground inheritance

`TextBrush` is the default ordinary-text foreground. Implicit styles cover Window, TextBlock, Label, ContentControl, Button, TextBox, RichTextBox, ToolTip, DataGrid, generated DataGrid text/edit elements, ListBox/ListView, ComboBox, CheckBox, RadioButton, GroupBox, TabItem, MenuItem, and Hyperlink.

`SubtleTextBrush` handles secondary text. `DisabledTextBrush` is a dedicated disabled-state foreground rather than relying on opacity or Windows defaults.

## Selected rows, inputs, and semantic state

Selected DataGrid rows use `SelectedRowBrush` / `SelectedRowTextBrush`. Generated DataGrid text follows the active cell foreground so selection never falls back to system black.

TextBox/RichTextBox text, caret, selection, backgrounds, borders, focus borders, and disabled state remain palette driven. Handoff, New Work, idle notes, and Missing BOL notes do not inject fixed foreground/caret colors.

Semantic state remains word-first and palette-assisted through warning, follow-up, completed, quiet, error, and information resources. View-models expose semantic state rather than WPF Brush/Color objects.

## Live switching

`ThemeManager.Apply(bool darkMode)` swaps the active palette dictionary. Base styles and DataTemplates remain in place. Because brushes and explicit shell backgrounds use dynamic resources, the visible application updates without restart or navigation reset.

Switching to Light mode immediately stops/collapses the ambient layer. Returning to Dark mode restarts it when the current WAA motion preference is enabled. Navigation, queue selection, search, notes, and Handoff drafts are not reset.

## Contrast requirements

Normal and important text combinations require at least **4.5:1**. Relevant boundaries/focus indicators require at least **3:1**. Ambient brushes are decorative only and never carry text/status meaning, so they do not replace contrast-tested foreground/background pairs.

A contrast failure is fixed by adjusting the palette or style; tests are not removed to hide it.

## Source and motion audit

Repository tests enforce that:

- Light/Dark palette key sets match
- every required palette key exists
- literal UI colors remain confined to palette dictionaries
- MainWindow/root client surfaces use dynamic `WindowBackgroundBrush`
- ambient brushes are centralized palette resources
- ambient layer is non-interactive and bounded to a fixed small number of motes
- Windows client-animation state can seed an unsaved first-run default but cannot disable the WAA motion control
- explicit WAA motion preference plus Dark mode control whether the ambient Storyboard runs
- no timer, blur, glow, or particle-engine path is introduced
- button motion remains restrained and render-only
- generated DataGrid/editor/button/selected/disabled text stays theme-safe
- only MainWindow is a top-level Window
- the central content host remains the one-window workspace

## Adding a new visual state

1. Prefer existing semantic/ordinary resources.
2. If a new color role is genuinely needed, add the same key to both Light and Dark palettes.
3. Reference it through `DynamicResource`.
4. Add the real text/boundary pair to contrast tests when applicable.
5. Do not expose Brush/Color from a view-model.
6. Do not add one-off literal colors to views.
7. Do not expand ambient motion beyond the bounded v0.4.5 layer without explicit user approval.