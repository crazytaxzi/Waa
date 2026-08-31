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
    private static readonly string ThemeRoot = Path.Combine(AppRoot, "Themes");
    private static readonly string LightPalettePath = Path.Combine(ThemeRoot, "LightColors.xaml");
    private static readonly string DarkPalettePath = Path.Combine(ThemeRoot, "DarkColors.xaml");
    private static readonly string BaseStylesPath = Path.Combine(ThemeRoot, "BaseStyles.xaml");

    private static readonly string[] RequiredBrushKeys =
    [
        "WindowBackgroundBrush", "PanelBackgroundBrush", "PanelSubtleBackgroundBrush", "HeaderBackgroundBrush",
        "BorderBrush", "ControlBorderBrush", "ControlBackgroundBrush",
        "ControlHoverBackgroundBrush", "ControlDisabledBackgroundBrush",
        "PrimaryBrush", "PrimaryHoverBrush", "PrimaryButtonTextBrush",
        "SuccessBrush", "SuccessHoverBrush", "SuccessButtonTextBrush",
        "TextBrush", "SubtleTextBrush", "DisabledTextBrush", "LinkTextBrush", "BreadcrumbTextBrush", "SelectionBrush",
        "DataGridRowBrush", "DataGridAlternateRowBrush", "DataGridHeaderBrush",
        "DataGridHeaderTextBrush", "DataGridGridLineBrush", "DataGridHoverRowBrush", "SelectedRowBrush",
        "SelectedRowTextBrush", "WarningTextBrush", "WarningBackgroundBrush",
        "FollowUpTextBrush", "FollowUpBackgroundBrush", "CompletedTextBrush",
        "CompletedBackgroundBrush", "QuietTextBrush", "QuietBackgroundBrush",
        "ErrorTextBrush", "ErrorBackgroundBrush", "InformationTextBrush",
        "InformationBackgroundBrush", "FocusBorderBrush"
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

        Assert.All(RequiredBrushKeys, key => Assert.Contains(key, light.Keys));
        Assert.All(RequiredBrushKeys, key => Assert.Contains(key, dark.Keys));
    }

    [Fact]
    public void DarkPalette_UsesStreamGunmetalPurpleAndGreenCore()
    {
        var dark = LoadPalette(DarkPalettePath);

        Assert.Equal("#11161B", dark["WindowBackgroundBrush"]);
        Assert.Equal("#1A232C", dark["PanelBackgroundBrush"]);
        Assert.Equal("#24303A", dark["PanelSubtleBackgroundBrush"]);
        Assert.Equal("#202A33", dark["HeaderBackgroundBrush"]);
        Assert.Equal("#B14DFF", dark["PrimaryBrush"]);
        Assert.Equal("#C779FF", dark["PrimaryHoverBrush"]);
        Assert.Equal("#39FF6A", dark["SuccessBrush"]);
        Assert.Equal("#63FF8A", dark["SuccessHoverBrush"]);
        Assert.Equal("#2D1F3A", dark["SelectedRowBrush"]);
        Assert.Equal("#39FF6A", dark["CompletedTextBrush"]);
    }

    [Fact]
    public void ThemeManager_SwapsOnlyTheActivePaletteDictionary()
    {
        var source = ReadAppFile("Infrastructure", "ThemeManager.cs");

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
    public void AppResources_MergePaletteBaseStylesAndWorkspaceTemplates()
    {
        var source = ReadAppFile("App.xaml");
        var requiredViews = new[]
        {
            "FleetQueueView", "DriverWorkspaceView", "IdleTaskView", "MissingBolTaskView",
            "WorkItemTaskView", "NewWorkView", "ActivityDetailView", "HandoffView",
            "UnmatchedBolView", "UnavailableView"
        };

        Assert.Contains("Themes/LightColors.xaml", source, StringComparison.Ordinal);
        Assert.Contains("Themes/BaseStyles.xaml", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", source, StringComparison.Ordinal);
        Assert.All(requiredViews, view => Assert.Contains($"views:{view}", source, StringComparison.Ordinal));
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
    public void DataGridGeneratedText_UsesCurrentCellForeground()
    {
        var styles = File.ReadAllText(BaseStylesPath);
        var views = ReadAppFile("Views", "FleetQueueView.xaml") +
                    ReadAppFile("Views", "UnmatchedBolView.xaml");
        var textColumns = Regex.Matches(views, "<DataGridTextColumn\\b", RegexOptions.CultureInvariant).Count;
        var dynamicStyles = Regex.Matches(
            views,
            "ElementStyle=\"\\{DynamicResource DataGridTextElementStyle\\}\"",
            RegexOptions.CultureInvariant).Count;

        Assert.NotEqual(0, textColumns);
        Assert.Equal(textColumns, dynamicStyles);
        Assert.Contains("RelativeSource AncestorType={x:Type DataGridCell}", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedRowsHeadersInputsButtonsAndToolTips_AreThemeAware()
    {
        var source = File.ReadAllText(BaseStylesPath);
        var requiredFragments = new[]
        {
            "SelectedRowTextBrush", "SelectedRowBrush", "DataGridHoverRowBrush", "DataGridHeaderTextBrush",
            "DataGridHeaderBrush", "TargetType=\"{x:Type TextBox}\"",
            "Property=\"CaretBrush\" Value=\"{DynamicResource TextBrush}\"",
            "Property=\"SelectionBrush\" Value=\"{DynamicResource SelectionBrush}\"",
            "x:Key=\"BaseButtonStyle\"", "x:Key=\"PrimaryButtonStyle\"", "x:Key=\"SuccessButtonStyle\"",
            "PrimaryButtonTextBrush", "SuccessButtonTextBrush", "x:Key=\"BreadcrumbTextStyle\"",
            "DisabledTextBrush", "TargetType=\"{x:Type ToolTip}\""
        };

        Assert.All(requiredFragments, fragment => Assert.Contains(fragment, source, StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticText_UsesDynamicThemeResources()
    {
        var source = File.ReadAllText(BaseStylesPath);
        foreach (var key in new[]
                 {
                     "WarningTextBrush", "FollowUpTextBrush", "CompletedTextBrush",
                     "QuietTextBrush", "ErrorTextBrush", "InformationTextBrush"
                 })
        {
            Assert.Contains($"{{DynamicResource {key}}}", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HandoffAndTaskEditors_RelyOnImplicitThemeAwareTextBoxStyle()
    {
        foreach (var file in new[] { "HandoffView.xaml", "IdleTaskView.xaml", "MissingBolTaskView.xaml", "NewWorkView.xaml" })
        {
            var source = ReadAppFile("Views", file);
            var textBoxes = Regex.Matches(
                    source,
                    "<TextBox\\b[^>]*>",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .Cast<Match>()
                .ToArray();
            Assert.NotEmpty(textBoxes);
            Assert.All(textBoxes, textBox =>
            {
                Assert.DoesNotMatch("Foreground\\s*=", textBox.Value);
                Assert.DoesNotMatch("CaretBrush\\s*=", textBox.Value);
            });
        }
    }

    [Fact]
    public void HandoffTaskAndWorkspaceViews_KeepThemeColorsCentralized()
    {
        foreach (var file in new[]
                 {
                     "FleetQueueView.xaml", "DriverWorkspaceView.xaml", "IdleTaskView.xaml", "MissingBolTaskView.xaml",
                     "WorkItemTaskView.xaml", "NewWorkView.xaml", "ActivityDetailView.xaml", "HandoffView.xaml",
                     "UnmatchedBolView.xaml", "UnavailableView.xaml"
                 })
        {
            var source = ReadAppFile("Views", file);
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", source);
            Assert.DoesNotContain("SolidColorBrush", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Brushes.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_UsesOneCentralContentHostInsteadOfSplitPane()
    {
        var source = ReadAppFile("MainWindow.xaml");
        var contentHosts = Regex.Matches(source, "<ContentControl\\b", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .ToArray();

        Assert.Single(contentHosts);
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
    public void ApplicationSource_HasNoProhibitedFixedThemeColors()
    {
        var paletteFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(LightPalettePath),
            Path.GetFullPath(DarkPalettePath)
        };
        var checks = new (string Name, Regex Pattern)[]
        {
            ("hex theme attribute", new Regex("(?:Foreground|Background|BorderBrush|CaretBrush|SelectionBrush)\\s*=\\s*\"#[0-9A-Fa-f]{3,8}\"", RegexOptions.CultureInvariant)),
            ("named fixed foreground", new Regex("Foreground\\s*=\\s*\"(?:Black|White|Gray|Grey|DarkGray|LightGray|Red|Green|Blue|Yellow|Orange)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("WPF Brushes member", new Regex("\\bBrushes\\.[A-Za-z]+", RegexOptions.CultureInvariant)),
            ("SolidColorBrush construction", new Regex("new\\s+SolidColorBrush\\s*\\(", RegexOptions.CultureInvariant)),
            ("fixed Color construction", new Regex("(?:Color\\.From|new\\s+Color\\s*\\()", RegexOptions.CultureInvariant)),
            ("static theme brush", new Regex("\\{StaticResource\\s+[A-Za-z0-9_]*Brush\\}", RegexOptions.CultureInvariant))
        };
        var violations = new List<string>();

        foreach (var path in AppSourceFiles().Where(path => !paletteFiles.Contains(Path.GetFullPath(path))))
        {
            var source = File.ReadAllText(path);
            foreach (var check in checks)
            {
                violations.AddRange(check.Pattern.Matches(source)
                    .Cast<Match>()
                    .Select(match => $"{Path.GetRelativePath(RepositoryRoot, path)}: {check.Name}: {match.Value}"));
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void LiteralHexThemeValues_ExistOnlyInPaletteFiles()
    {
        var violations = AppSourceFiles()
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
        var themeSource = ReadAppFile("Infrastructure", "ThemeManager.cs");
        var shellSource = ReadAppFile("MainWindow.xaml.cs");
        var handler = Regex.Match(
            shellSource,
            "private void OnThemeChanged\\([^)]*\\)\\s*\\{.*?\\n    \\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(handler.Success, "MainWindow theme-change handler was not found.");
        Assert.Contains("ApplyWindowTheme", handler.Value, StringComparison.Ordinal);
        Assert.Contains("UpdateThemeButtonText", handler.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeAsync", handler.Value, StringComparison.Ordinal);
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
        var palette = LoadPalette(Path.Combine(ThemeRoot, paletteFile));
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
            ("TextBrush", "HeaderBackgroundBrush"),
            ("SubtleTextBrush", "WindowBackgroundBrush"),
            ("SubtleTextBrush", "PanelBackgroundBrush"),
            ("SubtleTextBrush", "PanelSubtleBackgroundBrush"),
            ("PrimaryButtonTextBrush", "PrimaryBrush"),
            ("PrimaryButtonTextBrush", "PrimaryHoverBrush"),
            ("SuccessButtonTextBrush", "SuccessBrush"),
            ("SuccessButtonTextBrush", "SuccessHoverBrush"),
            ("SelectedRowTextBrush", "SelectedRowBrush"),
            ("SelectedRowTextBrush", "DataGridHoverRowBrush"),
            ("DataGridHeaderTextBrush", "DataGridHeaderBrush"),
            ("TextBrush", "ControlBackgroundBrush"),
            ("TextBrush", "ControlHoverBackgroundBrush"),
            ("DisabledTextBrush", "ControlDisabledBackgroundBrush"),
            ("WarningTextBrush", "WarningBackgroundBrush"),
            ("WarningTextBrush", "PanelBackgroundBrush"),
            ("FollowUpTextBrush", "FollowUpBackgroundBrush"),
            ("FollowUpTextBrush", "PanelBackgroundBrush"),
            ("CompletedTextBrush", "CompletedBackgroundBrush"),
            ("CompletedTextBrush", "PanelBackgroundBrush"),
            ("LinkTextBrush", "PanelBackgroundBrush"),
            ("BreadcrumbTextBrush", "PanelSubtleBackgroundBrush"),
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
                element => element.Attribute(x + "Key")?.Value ?? throw new InvalidDataException($"Palette brush without x:Key in {path}."),
                element => element.Attribute("Color")?.Value ?? throw new InvalidDataException($"Palette brush without Color in {path}."),
                StringComparer.Ordinal);
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminosity = RelativeLuminance(foreground);
        var backgroundLuminosity = RelativeLuminance(background);
        return (Math.Max(foregroundLuminosity, backgroundLuminosity) + 0.05d) /
               (Math.Min(foregroundLuminosity, backgroundLuminosity) + 0.05d);
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
        return (0.2126d * Linearize(red)) + (0.7152d * Linearize(green)) + (0.0722d * Linearize(blue));
    }

    private static double ParseChannel(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;

    private static double Linearize(double channel) =>
        channel <= 0.04045d ? channel / 12.92d : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

    private static IEnumerable<string> AppSourceFiles() =>
        Directory.EnumerateFiles(AppRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

    private static string ReadAppFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppRoot, .. parts]));

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
