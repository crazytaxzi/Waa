using System.Windows;
using System.Windows.Media;

namespace Waa.App.Infrastructure;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = "#F4F5F6",
            ["PanelBackgroundBrush"] = "#FFFFFF",
            ["PanelSubtleBackgroundBrush"] = "#F7F8F9",
            ["BorderBrush"] = "#D7DBDF",
            ["ControlBorderBrush"] = "#BFC6CC",
            ["ControlBackgroundBrush"] = "#FFFFFF",
            ["ControlHoverBrush"] = "#EEF2F4",
            ["ControlDisabledBrush"] = "#E8EBED",
            ["PrimaryBrush"] = "#22577A",
            ["PrimaryHoverBrush"] = "#194764",
            ["TextBrush"] = "#1F2933",
            ["SubtleTextBrush"] = "#5F6B73",
            ["SelectionBrush"] = "#A9D0E8",
            ["DataGridRowBrush"] = "#FFFFFF",
            ["DataGridAltRowBrush"] = "#FAFBFB",
            ["DataGridHeaderBrush"] = "#ECEFF1",
            ["DataGridHeaderTextBrush"] = "#253238",
            ["DataGridHorizontalLineBrush"] = "#E6E8EA",
            ["DataGridVerticalLineBrush"] = "#EEF0F1",
            ["SelectedRowBrush"] = "#DCEBF5",
            ["SelectedRowTextBrush"] = "#111111",
            ["WarningTextBrush"] = "#B22222",
            ["FollowUpTextBrush"] = "#8A6200",
            ["CompletedTextBrush"] = "#207A3C",
            ["QuietTextBrush"] = "#65717A"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = "#171A1D",
            ["PanelBackgroundBrush"] = "#202428",
            ["PanelSubtleBackgroundBrush"] = "#272C31",
            ["BorderBrush"] = "#3A4249",
            ["ControlBorderBrush"] = "#4A545C",
            ["ControlBackgroundBrush"] = "#2B3035",
            ["ControlHoverBrush"] = "#343B41",
            ["ControlDisabledBrush"] = "#252A2E",
            ["PrimaryBrush"] = "#3F7EA3",
            ["PrimaryHoverBrush"] = "#4D8FB7",
            ["TextBrush"] = "#E6E9EC",
            ["SubtleTextBrush"] = "#A7B0B7",
            ["SelectionBrush"] = "#456F89",
            ["DataGridRowBrush"] = "#202428",
            ["DataGridAltRowBrush"] = "#24292D",
            ["DataGridHeaderBrush"] = "#2B3136",
            ["DataGridHeaderTextBrush"] = "#E6E9EC",
            ["DataGridHorizontalLineBrush"] = "#343A40",
            ["DataGridVerticalLineBrush"] = "#2E3439",
            ["SelectedRowBrush"] = "#314F63",
            ["SelectedRowTextBrush"] = "#FFFFFF",
            ["WarningTextBrush"] = "#FF8A80",
            ["FollowUpTextBrush"] = "#FFD166",
            ["CompletedTextBrush"] = "#7BD88F",
            ["QuietTextBrush"] = "#A8B1B8"
        };

    public static event EventHandler? ThemeChanged;

    public static bool IsDarkMode { get; private set; }

    public static void Apply(bool darkMode)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("WAA theme cannot be applied before the application exists.");
        var palette = darkMode ? DarkPalette : LightPalette;

        foreach (var (key, colorText) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorText)!;
            application.Resources[key] = new SolidColorBrush(color);
        }

        IsDarkMode = darkMode;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
