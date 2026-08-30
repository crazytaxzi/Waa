using System.Windows;

namespace Waa.App.Infrastructure;

public static class ThemeManager
{
    public const string LightPaletteSource = "Themes/LightColors.xaml";
    public const string DarkPaletteSource = "Themes/DarkColors.xaml";

    public static bool IsDarkMode { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void Apply(bool darkMode)
    {
        IsDarkMode = darkMode;
        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            var dictionaries = resources.MergedDictionaries;
            var replacement = new ResourceDictionary
            {
                Source = new Uri(GetPaletteSource(darkMode), UriKind.Relative)
            };
            var currentIndex = FindPaletteIndex(dictionaries);
            if (currentIndex >= 0)
            {
                dictionaries[currentIndex] = replacement;
            }
            else
            {
                dictionaries.Insert(0, replacement);
            }
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string GetPaletteSource(bool darkMode) =>
        darkMode ? DarkPaletteSource : LightPaletteSource;

    private static int FindPaletteIndex(IList<ResourceDictionary> dictionaries)
    {
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is not null &&
                (source.EndsWith(LightPaletteSource, StringComparison.OrdinalIgnoreCase) ||
                 source.EndsWith(DarkPaletteSource, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }
}
