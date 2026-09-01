using Microsoft.Data.Sqlite;

namespace Waa.App.Data;

public sealed class ThemePreferenceStore
{
    private const string ThemeSettingKey = "appearance_theme";
    private const string AmbientMotionSettingKey = "appearance_ambient_motion";
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
        var value = GetSetting(ThemeSettingKey);
        return string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase);
    }

    public void SetDarkMode(bool darkMode) =>
        SetSetting(ThemeSettingKey, darkMode ? "dark" : "light");

    public bool? GetAmbientMotionPreference()
    {
        var value = GetSetting(AmbientMotionSettingKey);
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    public bool GetAmbientMotionEnabled() =>
        GetAmbientMotionPreference() ?? true;

    public void SetAmbientMotionEnabled(bool enabled) =>
        SetSetting(AmbientMotionSettingKey, enabled ? "on" : "off");

    private string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        EnsureSettingsTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        EnsureSettingsTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
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