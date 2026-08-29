using System.Globalization;

namespace Waa.App.Infrastructure;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _logDirectory;

    public static void Initialize(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logDirectory = logDirectory;
    }

    public static void Write(string message)
    {
        var directory = _logDirectory;
        if (directory is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                var path = Path.Combine(directory, $"waa-{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never take the application down.
        }
    }

    public static void Write(Exception exception, string context) =>
        Write($"{context}: {exception}");
}
