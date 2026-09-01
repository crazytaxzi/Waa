using System.Xml.Linq;
using Waa.App.Data;
using Xunit;

namespace Waa.App.Tests;

public sealed class AmbientMotionRegressionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string AppRoot = Path.Combine(RepositoryRoot, "src", "Waa.App");

    [Fact]
    public void MainWindow_AmbientLayerIsNonInteractiveThemeDrivenAndBounded()
    {
        var xaml = ReadAppFile("MainWindow.xaml");

        Assert.Contains("x:Key=\"AmbientMotionStoryboard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AmbientMotionLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource AmbientScanlineBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource AmbientParticleBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:19\"", xaml, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(xaml, "Fill=\"{DynamicResource AmbientParticleBrush}\""));
        Assert.DoesNotContain("BlurEffect", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbientMotion_OnlyRunsForDarkModeUserPreferenceAndWindowsAnimations()
    {
        var code = ReadAppFile("MainWindow.xaml.cs");

        Assert.Contains("_ambientMotionEnabled", code, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.IsDarkMode", code, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ClientAreaAnimation", code, StringComparison.Ordinal);
        Assert.Contains("AmbientMotionLayer.Visibility = Visibility.Visible", code, StringComparison.Ordinal);
        Assert.Contains("AmbientMotionLayer.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("storyboard.Begin", code, StringComparison.Ordinal);
        Assert.Contains("storyboard.Stop", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Timers", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbientMotionPreference_DefaultsOnAndPersistsWithoutSchemaChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaAmbientMotionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ThemePreferenceStore(Path.Combine(root, "waa.db"));

            Assert.True(store.GetAmbientMotionEnabled());
            store.SetAmbientMotionEnabled(false);
            Assert.False(store.GetAmbientMotionEnabled());
            store.SetAmbientMotionEnabled(true);
            Assert.True(store.GetAmbientMotionEnabled());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PaletteDictionaries_ContainMatchingAmbientBrushRoles()
    {
        var light = LoadPalette("LightColors.xaml");
        var dark = LoadPalette("DarkColors.xaml");

        foreach (var key in new[] { "AmbientParticleBrush", "AmbientScanlineBrush" })
        {
            Assert.Contains(key, light.Keys);
            Assert.Contains(key, dark.Keys);
        }

        Assert.Equal("#65CFFF", dark["AmbientParticleBrush"]);
        Assert.Equal("#4DBFFF", dark["AmbientScanlineBrush"]);
    }

    [Fact]
    public void Buttons_UseRestrainedRenderOnlyHoverMotion()
    {
        var styles = ReadAppFile("Themes", "BaseStyles.xaml");

        Assert.Contains("RenderTransformOrigin", styles, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform ScaleX=\"1\" ScaleY=\"1\"", styles, StringComparison.Ordinal);
        Assert.Contains("RoutedEvent=\"MouseEnter\"", styles, StringComparison.Ordinal);
        Assert.Contains("RoutedEvent=\"MouseLeave\"", styles, StringComparison.Ordinal);
        Assert.Contains("To=\"1.012\"", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsPressed\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateTransform", styles, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> LoadPalette(string fileName)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var path = Path.Combine(AppRoot, "Themes", fileName);
        return XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                element => element.Attribute(x + "Key")?.Value ?? throw new InvalidDataException("Palette key missing."),
                element => element.Attribute("Color")?.Value ?? throw new InvalidDataException("Palette color missing."),
                StringComparer.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

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