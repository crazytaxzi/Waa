using System.Text.RegularExpressions;
using Xunit;

namespace Waa.App.Tests;

public sealed class RunBindingSafetyTests
{
    [Fact]
    public void DataBoundRunText_IsExplicitlyOneWay()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Waa.App");
        var pattern = new Regex(
            "<Run\\b[^>]*\\bText\\s*=\\s*\"(?<binding>\\{Binding[^\"]*\\})\"[^>]*/?>",
            RegexOptions.CultureInvariant);
        var violations = new List<string>();
        var bindingCount = 0;

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in pattern.Matches(source))
            {
                bindingCount++;
                var binding = match.Groups["binding"].Value;
                if (binding.Contains("Mode=OneWay", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add(
                    $"{Path.GetRelativePath(repositoryRoot, path)}:{line} data-bound Run.Text must declare Mode=OneWay: {binding}");
            }
        }

        Assert.NotEqual(0, bindingCount);
        Assert.Empty(violations);
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
