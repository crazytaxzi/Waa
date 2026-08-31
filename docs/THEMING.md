# WAA Theming v0.4.4

## Meaning of Auto text

“Auto” text in WAA means ordinary UI text automatically follows the currently selected Light or Dark palette through WPF resource inheritance and `DynamicResource` references.

It does **not** mean an OS-synchronized/System appearance option. WAA keeps the existing explicit Light mode / Dark mode choice and stores that preference locally.

## Resource ownership

Theme color ownership is centralized:

- `Themes/LightColors.xaml` — literal colors for Light mode
- `Themes/DarkColors.xaml` — literal colors for Dark mode
- `Themes/BaseStyles.xaml` — control styles that consume palette keys through `DynamicResource`
- `App.xaml` — merges the active palette plus base styles and workspace DataTemplates
- `ThemeManager` — replaces only the active palette dictionary

Literal theme colors are not allowed in MainWindow, UserControls, view-models, converters, or task templates.

The Light and Dark dictionaries must contain matching key sets. Missing keys are a test failure.

## v0.4.3 stream palette

The v0.4.3 presentation refresh keeps gunmetal/neutral surfaces as the visual foundation and uses the stream palette as restrained semantic accents rather than decorative fills.

Dark-mode core surfaces are:

- app background `#11161B`
- panel background `#1A232C`
- raised/subtle panel `#24303A`
- header chrome `#202A33`

Accent ownership is semantic:

- purple is the primary accent for selection, focus, breadcrumbs, Handoff, and highlighted actions
- green is the success/positive accent for completed state and `Next Needing Attention`
- ordinary body text remains neutral/light rather than neon

Primary purple uses `PrimaryBrush` / `PrimaryHoverBrush`; positive green uses `SuccessBrush` / `SuccessHoverBrush`. The requested neon fills use a dark button foreground where needed so normal-text contrast remains compliant.

There is no glow, blur, animation, gradient, or other decorative effect. Light mode keeps the same purple/green semantic roles on light neutral surfaces rather than forcing gunmetal backgrounds into the light palette.

## v0.4.4 shell background correction

`MainWindow` and its root client Grid explicitly bind their backgrounds to `WindowBackgroundBrush` through `DynamicResource`.

This closes the exposed shell/margin gap where a Windows/default light surface could remain visible even while the rest of WAA had switched to the dark palette. The correction does not introduce a separate shell color or code-behind brush; the whole client background uses the same centralized palette role as the rest of the application and updates during live Light/Dark switching.

The title bar continues to use the existing DWM helper where supported.

## Ordinary foreground inheritance

`TextBrush` is the default ordinary-text foreground. Implicit styles make it the normal foreground for text-bearing WPF controls rather than requiring every TextBlock to set a foreground individually.

Theme-aware implicit/base styles currently cover the controls used by WAA, including:

- Window
- TextBlock
- Label
- ContentControl
- Button
- TextBox
- RichTextBox
- ToolTip
- DataGrid, DataGridRow, DataGridCell, DataGridColumnHeader
- DataGridTextColumn generated display/edit elements
- ListBox/ListBoxItem
- ListView/ListViewItem
- ComboBox/ComboBoxItem
- CheckBox
- RadioButton
- GroupBox
- TabItem
- MenuItem
- Hyperlink

`SubtleTextBrush` is used for secondary text. `DisabledTextBrush` is a dedicated disabled-state foreground; disabled readability does not rely on opacity or a Windows system default.

## Buttons, breadcrumbs, hover, and selected rows

Purple primary controls use:

- `PrimaryBrush` / `PrimaryButtonTextBrush`
- `PrimaryHoverBrush` / `PrimaryButtonTextBrush`

Green positive controls use:

- `SuccessBrush` / `SuccessButtonTextBrush`
- `SuccessHoverBrush` / `SuccessButtonTextBrush`

`Handoff` uses the purple primary style. `Next Needing Attention` uses the green success style. Theme-mode and `Update Reports` buttons remain neutral controls.

Breadcrumb text uses `BreadcrumbTextBrush`. Focus boundaries use `FocusBorderBrush`. Fleet row hover uses the dedicated `DataGridHoverRowBrush` rather than a one-off view color.

The base button hover style updates the Button background property rather than overriding a named border inside the template. This lets both accent styles override hover background correctly and prevents a generic hover surface from being combined with accent-button text.

Selected DataGrid rows use:

- `SelectedRowBrush`
- `SelectedRowTextBrush`

DataGrid cells inherit selected-row text state, and `DataGridTextColumn` uses explicit reusable dynamic element/editing styles so generated elements do not fall back to a WPF system foreground.

The Fleet Queue uses compact row/cell styles derived from the central DataGrid styles. Density changes alter only row minimum height and cell padding; they do not bypass centralized selected/hover/focus/text resources or virtualization.

## Inputs

TextBox/RichTextBox text, caret, selection, backgrounds, borders, focus borders, and disabled state are palette driven.

Handoff, New Work, idle note, and Missing BOL note editors rely on the implicit TextBox style; their XAML does not inject fixed foreground/caret colors.

## Semantic state

View-models expose semantic state rather than WPF Brush objects. XAML styles/triggers translate semantic state into palette resources.

Current semantic palette pairs include:

- `WarningTextBrush` / `WarningBackgroundBrush`
- `FollowUpTextBrush` / `FollowUpBackgroundBrush`
- `CompletedTextBrush` / `CompletedBackgroundBrush`
- `QuietTextBrush` / `QuietBackgroundBrush`
- `ErrorTextBrush` / `ErrorBackgroundBrush`
- `InformationTextBrush` / `InformationBackgroundBrush`

Completed/positive state uses the green palette role where appropriate. Status remains understandable through words first; color is supplemental.

## Live switching

`ThemeManager.Apply(bool darkMode)` loads the selected palette ResourceDictionary and replaces the current palette entry in `Application.Resources.MergedDictionaries`. Base styles and DataTemplates stay in place.

Because style brushes and the explicit MainWindow/root-shell backgrounds are dynamic resources, visible controls and the full client background update immediately without application restart or route recreation. Theme switching does not reset navigation, queue selection, search, notes, or Handoff draft state.

The Windows title bar is updated through the existing DWM helper where supported.

Theme preference persistence remains SQLite-backed. The preference write runs off the UI thread; if it fails, WAA restores the prior visible theme and reports the error rather than leaving an unsaved appearance active.

## Contrast requirements

Automated palette tests calculate WCAG-style relative luminance/contrast deterministically from the actual palette values.

Normal and important text combinations require at least **4.5:1**, including:

- `TextBrush` on window/panel/subtle/header surfaces
- `SubtleTextBrush` on its actual surfaces
- ordinary button text on normal and hover control backgrounds
- `PrimaryButtonTextBrush` on `PrimaryBrush` and `PrimaryHoverBrush`
- `SuccessButtonTextBrush` on `SuccessBrush` and `SuccessHoverBrush`
- selected-row and fleet-hover text/background
- DataGrid header text/background
- TextBox/editor text/background
- disabled text/background
- warning/follow-up/completed semantic text on its semantic and panel surfaces
- link and breadcrumb text on their actual surfaces
- quiet/information semantic pairs

Important UI boundaries/focus indicators require at least **3:1** where applicable, including panel/control borders and focus border against their surfaces.

A contrast failure is fixed by adjusting the palette or style; tests are not removed to hide it. Recommended palette values may therefore be adapted when the literal value would fail the actual foreground/background pair.

## Source audit test

The repository-level theme audit scans application `.xaml` and `.cs` files, not just one screen. Outside the explicitly allowed palette files it rejects inappropriate fixed color patterns including:

- hard-coded foreground/background/border/caret/selection hex values
- named fixed foregrounds such as Black/White/Gray
- `Brushes.*` UI color use
- arbitrary `new SolidColorBrush(...)`
- fixed Color construction
- theme brush use through `StaticResource` where live switching requires `DynamicResource`
- literal hex theme values outside the palette dictionaries

The audit also checks:

- Light/Dark key sets match
- every required palette key exists
- the v0.4.3 stream palette retains the expected gunmetal/purple/green roles
- ThemeManager does not recreate brushes in C#
- App.xaml uses palette + base style dictionaries
- generated DataGrid text is theme-aware
- Handoff/task/workspace XAML contains no one-off literal theme colors
- MainWindow and its root client surface explicitly use the dynamic `WindowBackgroundBrush`
- only MainWindow is a top-level Window
- the central content host replaced the legacy split pane

## Adding a new visual state

When new UI requires a color:

1. Prefer existing semantic/ordinary resources.
2. If a genuinely new theme role is needed, add the same key to both Light and Dark palettes.
3. Reference the key through `DynamicResource` in BaseStyles or the focused view.
4. Add the real foreground/background pair to contrast tests.
5. Do not expose Brush/Color from a view-model.
6. Do not add a one-off literal color to a view.

This keeps Light and Dark mode one coherent system instead of two slowly diverging sets of exceptions.
