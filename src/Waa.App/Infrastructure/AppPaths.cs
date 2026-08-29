namespace Waa.App.Infrastructure;

public sealed record AppPaths(string DataDirectory, string DatabasePath, string LogDirectory)
{
    public static AppPaths Create()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows did not provide a Local Application Data folder.");
        }

        var dataDirectory = Path.Combine(localAppData, "WAA");
        var logDirectory = Path.Combine(dataDirectory, "logs");
        return new AppPaths(dataDirectory, Path.Combine(dataDirectory, "waa.db"), logDirectory);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
