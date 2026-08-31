using Xunit;

namespace Waa.App.Tests;

public sealed class ShellThemeRegressionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string MainWindowPath = Path.Combine(
        RepositoryRoot,
        "src",
        "Waa.App",
        "MainWindow.xaml");

    [Fact]
    public void MainWindowAndRootClientSurface_ExplicitlyFollowWindowBackgroundBrush()
    {
        var source = File.ReadAllText(MainWindowPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "Background=\"{DynamicResource WindowBackgroundBrush}\"\n        WindowStartupLocation",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Grid Margin=\"12\"\n          Background=\"{DynamicResource WindowBackgroundBrush}\"",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Background=\"{StaticResource WindowBackgroundBrush}\"",
            source,
            StringComparison.Ordinal);
    }

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
