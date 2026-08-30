using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Waa.App.Infrastructure;
using Xunit;

namespace Waa.App.Tests;

public sealed class ThemeAndSourceAuditTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string AppRoot = Path.Combine(RepositoryRoot, "src", "Waa.App");
    private static readonly string LightPalettePath = Path.Combine(AppRoot, "Themes", "LightColors.xaml");
    private static readonly string DarkPalettePath = Path.Combine(AppRoot, "Themes", "DarkColors.xaml");
    private static readonly string BaseStylesPath = Path.Combine(AppRoot, "Themes", "BaseStyles.xaml");

    private static readonly string[] RequiredBrushKeys =
    [
        "WindowBackgroundBrush",
        "PanelBackgroundBrush",
        "PanelSubtleBackgroundBrush",
        "BorderBrush",
        "ControlBorderBrush",
        "ControlBackgroundBrush",
        "ControlHoverBackgroundBrush",
        "ControlDisabledBackgroundBrush",
        "PrimaryBrush",
        "PrimaryHoverBrush",
        "TextBrush",
        "SubtleTextBrush",
        "DisabledTextBrush",
        "PrimaryButtonTextBrush",
        "LinkTextBrush",
        "SelectionBrush",
        "DataGridRowBrush",
        "DataGridAlternateRowBrush",
        "DataGridHeaderBrush",
        "DataGridHeaderTextBrush",
        "DataGridGridLineBrush",
        "SelectedRowBrush",
        "SelectedRowTextBrush",
        "WarningTextBrush",
        "WarningBackgroundBrush",
        "FollowUpTextBrush",
        "FollowUpBackgroundBrush",
        "CompletedTextBrush",
        "CompletedBackgroundBrush",
        "QuietTextBrush",
        "QuietBackgroundBrush",
        "ErrorTextBrush",
        "ErrorBackgroundBrush",
        "InformationTextBrush",
        "InformationBackgroundBrush",
        "FocusBorderBrush"
    ];

    [Fact]
    public void ThemeDictionaries_ContainMatchingKeySets()
    {
        var light = LoadPalette(LightPalettePath);
        var dark = LoadPalette(DarkPalettePath);

        Assert.Equal(
            light.Keys.OrderBy(key => key, StringComparer.Ordinal),
            dark.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void ThemeDictionaries_ContainEveryRequiredBrushKey()
    {
        var light = LoadPalette(LightPalettePath);
        var dark = LoadPalette(DarkPalettePath);

        Assert.All(RequiredBrushKeys, key => Assert.True(light.ContainsKey(key), $"Light palette is missing {key}."));
        Assert.All(RequiredBrushKeys, key => Assert.True(dark.ContainsKey(key), $"Dark palette is missing {key}."));
    }

    [Fact]
    public void ThemeManager_SwapsOnlyTheActivePaletteDictionary()
    {
        var source = File.ReadAllText(Path.Combine(AppRoot, "Infrastructure", "ThemeManager.cs"));

        Assert.Contains("MergedDictionaries", source, StringComparison.Ordinal);
        Assert.Contains("LightPaletteSource", source, StringComparison.Ordinal);
        Assert.Contains("DarkPaletteSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentWorkspace", source, StringComparison.Ordinal);
        Assert.Equal("Themes/LightColors.xaml", ThemeManager.GetPaletteSource(darkMode: false));
        Assert.Equal("Themes/DarkColors.xaml", ThemeManager.GetPaletteSource(darkMode: true));
    }

    [Fact]
    public void AppResources_MergeOnePaletteAndOneBaseStyleDictionary()
    {
        var source = File.ReadAllText(Path.Combine(AppRoot, "App.xaml"));

        Assert.Contains("Themes/LightColors.xaml", source, StringComparison.Ordinal);
        Assert.Contains("Themes/BaseStyles.xaml", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryText_UsesImplicitThemeInheritance()
    {
        var source = File.ReadAllText(BaseStylesPath);

        Assert.Contains("TargetType=\"{x:Type Window}\"", source, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type TextBlock}\"", source, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type Label}\"", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource TextBrush}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DataGridGeneratedText_UsesTheCurrentCellForeground()
    {
        var styles = File.ReadAllText(BaseStylesPath);
        var gridViews = File.ReadAllText(Path.Combine(AppRoot, "Views", "FleetQueueView.xaml")) +
                        File.ReadAllText(Path.Combine(AppRoot, "Views", "UnmatchedBolView.xaml"));
        var textColumns = Regex.Matches(gridViews, "<DataGridTextColumn\\b", RegexOptions.CultureInvariant).Count;
        var dynamicElementStyles = Regex.Matches(
            gridViews,
            "ElementStyle=\"\\{DynamicResource DataGridTextElementStyle\\}\"",
            RegexOptions.CultureInvariant).Count;

        Assert.True(textColumns > 0);
        Assert.Equal(textColumns, dynamicElementStyles);
        Assert.Contains("RelativeSource AncestorType={x:Type DataGridCell}", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedRowsAndHeaders_UseThemeAwareForegrounds()
    {
        var source = File.ReadAllText(BaseStylesPath);

        Assert.Contains("SelectedRowTextBrush", source, StringComparison.Ordinal);
        Assert.Contains("DataGridHeaderTextBrush", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SelectedRowBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DataGridHeaderBrush}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TextInputs_UseThemeAwareTextCaretSelectionAndDisabledState()
    {
        var source = File.ReadAllText(BaseStylesPath);

        Assert.Contains("TargetType=\"{x:Type TextBox}\"", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"CaretBrush\" Value=\"{DynamicResource TextBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("Property=\"SelectionBrush\" Value=\"{DynamicResource SelectionBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledTextBrush}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_UseThemeAwareOrdinaryPrimaryAndDisabledText()
    {
        var source = File.ReadAllText(BaseStylesPath);

        Assert.Contains("x:Key=\"BaseButtonStyle\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PrimaryButtonStyle\"", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonTextBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledTextBrush}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticText_UsesDynamicThemeResources()
    {
        var source = File.ReadAllText(BaseStylesPath);

        foreach (var key in new[]
                 {
                     "WarningTextBrush",
                     "FollowUpTextBrush",
                     "CompletedTextBrush",
                     "QuietTextBrush",
                     "ErrorTextBrush",
                     "InformationTextBrush"
                 })
        {
            Assert.Contains($"{{DynamicResource {key}}}", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ToolTips_UseThemeAwareTextAndSurface()
    {
        var source = File.ReadAllText(BaseStylesPath);

        Assert.Contains("TargetType=\"{x:Type ToolTip}\"", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PanelBackgroundBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource TextBrush}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffAndTaskEditors_RelyOnThemeAwareTextBoxStyle()
    {
        var files = new[]
        {
            "HandoffView.xaml",
            "IdleTaskView.xaml",
            "MissingBolTaskView.xaml",
            "NewWorkView.xaml"
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(AppRoot, "Views", file));
            var textBoxes = Regex.Matches(
                source,
                "<TextBox\\b[^>]*>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.NotEmpty(textBoxes);
            foreach (Match textBox in textBoxes)
            {
                Assert.False(Regex.IsMatch(textBox.Value, "Foreground\\s*=", RegexOptions.CultureInvariant));
                Assert.False(Regex.IsMatch(textBox.Value, "CaretBrush\\s*=", RegexOptions.CultureInvariant));
            }
        }
    }

    [Fact]
    public void MainWindow_UsesOneCentralContentHostInsteadOfSplitPane()
    {
        var source = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));

        Assert.Equal(1, Regex.Matches(source, "<ContentControl\\b", RegexOptions.CultureInvariant).Count);
        Assert.Contains("Content=\"{Binding CurrentWorkspace}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected Driver", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GridSplitter", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryDefinesOnlyOneTopLevelWindow()
    {
        var windowXaml = Directory.EnumerateFiles(AppRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).TrimStart().StartsWith("<Window", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();
        var windowClasses = Directory.EnumerateFiles(AppRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "class\\s+\\w+\\s*:\\s*Window",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(_ => Path.GetFileName(path)))
            .ToArray();

        Assert.Equal(new[] { "MainWindow.xaml" }, windowXaml);
        Assert.Equal(new[] { "MainWindow.xaml.cs" }, windowClasses);
    }

    [Fact]
    public void WorkspaceDataTemplates_CoverEveryCentralRouteView()
    {
        var source = File.ReadAllText(Path.Combine(AppRoot, "App.xaml"));
        var requiredViews = new[]
        {
            "FleetQueueView",
            "DriverWorkspaceView",
            "IdleTaskView",
            "MissingBolTaskView",
            "WorkItemTaskView",
            "NewWorkView",
            "ActivityDetailView",
            "HandoffView",
            "UnmatchedBolView",
            "UnavailableView"
        };

        Assert.All(requiredViews, view => Assert.Contains($"views:{view}", source, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationSource_HasNoProhibitedFixedThemeColors()
    {
        var paletteFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(LightPalettePath),
            Path.GetFullPath(DarkPalettePath)
        };
        var violations = new List<string>();
        var checks = new (string Name, Regex Pattern)[]
        {
            ("hex theme attribute", new Regex("(?:Foreground|Background|BorderBrush|CaretBrush|SelectionBrush)\\s*=\\s*\"#[0-9A-Fa-f]{3,8}\"", RegexOptions.CultureInvariant)),
            ("named fixed foreground", new Regex("Foreground\\s*=\\s*\"(?:Black|White|Gray|Grey|DarkGray|LightGray|Red|Green|Blue|Yellow|Orange)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("WPF Brushes member", new Regex("\\bBrushes\\.[A-Za-z]+", RegexOptions.CultureInvariant)),
            ("SolidColorBrush construction", new Regex("new\\s+SolidColorBrush\\s*\\(", RegexOptions.CultureInvariant)),
            ("fixed Color construction", new Regex("(?:Color\\.From|new\\s+Color\\s*\\()", RegexOptions.CultureInvariant)),
            ("static theme brush", new Regex("\\{StaticResource\\s+[A-Za-z0-9_]*Brush\\}", RegexOptions.CultureInvariant))
        };

        foreach (var path in Directory.EnumerateFiles(AppRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            if (paletteFiles.Contains(Path.GetFullPath(path)))
            {
                continue;
            }

            var source = File.ReadAllText(path);
            foreach (var check in checks)
            {
                foreach (Match match in check.Pattern.Matches(source))
                {
                    violations.Add($"{Path.GetRelativePath(RepositoryRoot, path)}: {check.Name}: {match.Value}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void LiteralHexThemeValues_ExistOnlyInPaletteFiles()
    {
        var violations = Directory.EnumerateFiles(AppRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Equals(LightPalettePath, StringComparison.OrdinalIgnoreCase) &&
                           !path.Equals(DarkPalettePath, StringComparison.OrdinalIgnoreCase))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "#[0-9A-Fa-f]{3,8}", RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ThemeSwitching_DoesNotRequireRestartOrNavigationReset()
    {
        var themeSource = File.ReadAllText(Path.Combine(AppRoot, "Infrastructure", "ThemeManager.cs"));
        var shellSource = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var changeHandler = Regex.Match(
            shellSource,
            "private void OnThemeChanged\\([^)]*\\)\\s*\\{.*?\\n    \\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(changeHandler.Success, "MainWindow theme change handler was not found.");
        Assert.Contains("ApplyWindowTheme", changeHandler.Value, StringComparison.Ordinal);
        Assert.Contains("UpdateThemeButtonText", changeHandler.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeAsync", changeHandler.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate", themeSource, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredContrastPairs))]
    public void PaletteContrast_MeetsRequiredRatio(
        string paletteFile,
        string foregroundKey,
        string backgroundKey,
        double minimumRatio)
    {
        var palette = LoadPalette(Path.Combine(AppRoot, "Themes", paletteFile));
        var actual = ContrastRatio(palette[foregroundKey], palette[backgroundKey]);

        Assert.True(
            actual >= minimumRatio,
            FormattableString.Invariant(
                $"{paletteFile}: {foregroundKey} on {backgroundKey} is {actual:0.00}:1; required {minimumRatio:0.0}:1."));
    }

    public static IEnumerable<object[]> RequiredContrastPairs()
    {
        var normalTextPairs = new (string Foreground, string Background)[]
        {
            ("TextBrush", "WindowBackgroundBrush"),
            ("TextBrush", "PanelBackgroundBrush"),
            ("TextBrush", "PanelSubtleBackgroundBrush"),
            ("SubtleTextBrush", "WindowBackgroundBrush"),
            ("SubtleTextBrush", "PanelBackgroundBrush"),
            ("SubtleTextBrush", "PanelSubtleBackgroundBrush"),
            ("PrimaryButtonTextBrush", "PrimaryBrush"),
            ("SelectedRowTextBrush", "SelectedRowBrush"),
            ("DataGridHeaderTextBrush", "DataGridHeaderBrush"),
            ("TextBrush", "ControlBackgroundBrush"),
            ("DisabledTextBrush", "ControlDisabledBackgroundBrush"),
            ("WarningTextBrush", "WarningBackgroundBrush"),
            ("WarningTextBrush", "PanelBackgroundBrush"),
            ("FollowUpTextBrush", "FollowUpBackgroundBrush"),
            ("FollowUpTextBrush", "PanelBackgroundBrush"),
            ("CompletedTextBrush", "CompletedBackgroundBrush"),
            ("CompletedTextBrush", "PanelBackgroundBrush"),
            ("LinkTextBrush", "PanelBackgroundBrush"),
            ("QuietTextBrush", "QuietBackgroundBrush"),
            ("InformationTextBrush", "InformationBackgroundBrush")
        };

        foreach (var palette in new[] { "LightColors.xaml", "DarkColors.xaml" })
        {
            foreach (var pair in normalTextPairs)
            {
                yield return [palette, pair.Foreground, pair.Background, 4.5d];
            }

            yield return [palette, "BorderBrush", "PanelBackgroundBrush", 3.0d];
            yield return [palette, "ControlBorderBrush", "ControlBackgroundBrush", 3.0d];
            yield return [palette, "FocusBorderBrush", "PanelBackgroundBrush", 3.0d];
        }
    }

    private static IReadOnlyDictionary<string, string> LoadPalette(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                element => element.Attribute(x + "Key")?.Value ??
                           throw new InvalidDataException($"Palette brush without x:Key in {path}."),
                element => element.Attribute("Color")?.Value ??
                           throw new InvalidDataException($"Palette brush without Color in {path}."),
                StringComparer.Ordinal);
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminosity = RelativeLuminance(foreground);
        var backgroundLuminosity = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminosity, backgroundLuminosity);
        var darker = Math.Min(foregroundLuminosity, backgroundLuminosity);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(string color)
    {
        var normalized = color.TrimStart('#');
        if (normalized.Length == 8)
        {
            normalized = normalized[2..];
        }

        if (normalized.Length != 6)
        {
            throw new InvalidDataException($"Unsupported palette color '{color}'.");
        }

        var red = ParseChannel(normalized[0..2]);
        var green = ParseChannel(normalized[2..4]);
        var blue = ParseChannel(normalized[4..6]);
        return (0.2126d * Linearize(red)) +
               (0.7152d * Linearize(green)) +
               (0.0722d * Linearize(blue));
    }

    private static double ParseChannel(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;

    private static double Linearize(double channel) =>
        channel <= 0.04045d
            ? channel / 12.92d
            : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Waa.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Waa.sln from the test output directory.");
    }
}
