using Microsoft.Data.Sqlite;

namespace Waa.App.Data;

public sealed class ThemePreferenceStore
{
    private const string ThemeSettingKey = "appearance_theme";
    private readonly string _connectionString;

    public ThemePreferenceStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString();
    }

    public bool GetDarkMode()
    {
        using var connection = OpenConnection();
        EnsureSettingsTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", ThemeSettingKey);
        var value = command.ExecuteScalar() as string;
        return string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase);
    }

    public void SetDarkMode(bool darkMode)
    {
        using var connection = OpenConnection();
        EnsureSettingsTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", ThemeSettingKey);
        command.Parameters.AddWithValue("$value", darkMode ? "dark" : "light");
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void EnsureSettingsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
