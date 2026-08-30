using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Waa.App.Tests;

public sealed class InteractionRegressionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string AppRoot = Path.Combine(RepositoryRoot, "src", "Waa.App");

    [Fact]
    public void FleetQueue_RowClickAndEnterOpenDriverWorkspace()
    {
        var xaml = ReadAppFile("Views", "FleetQueueView.xaml");
        var codeBehind = ReadAppFile("Views", "WorkspaceViews.xaml.cs");

        Assert.Contains("PreviewMouseLeftButtonUp=\"OnFleetGridMouseLeftButtonUp\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDown=\"OnFleetGridPreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Enter", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenDriverCommand", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void AltLeftBack_DoesNotHijackTextEditingControls()
    {
        var xaml = ReadAppFile("MainWindow.xaml");
        var codeBehind = ReadAppFile("MainWindow.xaml.cs");

        Assert.Contains("PreviewKeyDown=\"OnPreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Left", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Alt", codeBehind, StringComparison.Ordinal);
        Assert.Contains("IsTextEditingControl", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TextBoxBase or PasswordBox", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackCommand", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeToggle_PersistsOffUiThreadAndRollsBackVisibleThemeOnFailure()
    {
        var codeBehind = ReadAppFile("MainWindow.xaml.cs");

        Assert.Contains("await Task.Run(() => _themePreferenceStore.SetDarkMode(next))", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.Apply(previous)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ThemeToggleButton.IsEnabled = false", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ThemeToggleButton.IsEnabled = true", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryButtonHover_IsNotOverriddenInsideBaseControlTemplate()
    {
        var styles = ReadAppFile("Themes", "BaseStyles.xaml");

        Assert.DoesNotContain(
            "<Setter TargetName=\"ButtonBorder\" Property=\"Background\" Value=\"{DynamicResource ControlHoverBackgroundBrush}\" />",
            styles,
            StringComparison.Ordinal);
        Assert.Contains("<Style.Triggers>", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource ControlHoverBackgroundBrush}\"", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource PrimaryHoverBrush}\"", styles, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LightColors.xaml", "TextBrush", "ControlHoverBackgroundBrush", 4.5d)]
    [InlineData("DarkColors.xaml", "TextBrush", "ControlHoverBackgroundBrush", 4.5d)]
    [InlineData("LightColors.xaml", "PrimaryButtonTextBrush", "PrimaryHoverBrush", 4.5d)]
    [InlineData("DarkColors.xaml", "PrimaryButtonTextBrush", "PrimaryHoverBrush", 4.5d)]
    public void HoverTextContrast_MeetsNormalTextRequirement(
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
