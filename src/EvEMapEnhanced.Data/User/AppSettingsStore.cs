using System.Globalization;
using EvEMapEnhanced.Core.Stats;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>Persisted user preferences (key/value in the local SQLite user DB).</summary>
public sealed class AppSettingsStore
{
    private const string ZKillboardRequestModeKey = "ZKillboardRequestMode";
    private const string ZKillboardScopeKey = "ZKillboardScope";
    private const string ShowEveScoutWormholesKey = "ShowEveScoutWormholes";
    private const string UseWormholesInRoutingKey = "UseWormholesInRouting";
    private const string UseZarzakhInRoutingKey = "UseZarzakhInRouting";
    private readonly string _sqlitePath;

    public AppSettingsStore(string sqlitePath) => _sqlitePath = sqlitePath;

    public ZKillboardRequestMode GetZKillboardRequestMode()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ZKillboardRequestModeKey);
        if (cmd.ExecuteScalar() is not string value)
            return ZKillboardRequestMode.Polite;

        return Enum.TryParse<ZKillboardRequestMode>(value, ignoreCase: true, out var mode)
            ? mode
            : ZKillboardRequestMode.Polite;
    }

    public void SetZKillboardRequestMode(ZKillboardRequestMode mode)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", ZKillboardRequestModeKey);
        cmd.Parameters.AddWithValue("$value", ((int)mode).ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public ZKillboardScope GetZKillboardScope()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ZKillboardScopeKey);
        if (cmd.ExecuteScalar() is not string value)
            return ZKillboardScope.JumpRange;

        return Enum.TryParse<ZKillboardScope>(value, ignoreCase: true, out var scope)
            ? scope
            : ZKillboardScope.JumpRange;
    }

    public void SetZKillboardScope(ZKillboardScope scope)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", ZKillboardScopeKey);
        cmd.Parameters.AddWithValue("$value", ((int)scope).ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public bool GetShowEveScoutWormholes()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ShowEveScoutWormholesKey);
        if (cmd.ExecuteScalar() is not string value)
            return true;

        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void SetShowEveScoutWormholes(bool enabled)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", ShowEveScoutWormholesKey);
        cmd.Parameters.AddWithValue("$value", enabled ? "1" : "0");
        cmd.ExecuteNonQuery();
    }

    public bool GetUseWormholesInRouting()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", UseWormholesInRoutingKey);
        if (cmd.ExecuteScalar() is not string value)
            return false;

        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void SetUseWormholesInRouting(bool enabled)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", UseWormholesInRoutingKey);
        cmd.Parameters.AddWithValue("$value", enabled ? "1" : "0");
        cmd.ExecuteNonQuery();
    }

    public bool GetUseZarzakhInRouting()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", UseZarzakhInRoutingKey);
        if (cmd.ExecuteScalar() is not string value)
            return true;

        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void SetUseZarzakhInRouting(bool enabled)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", UseZarzakhInRoutingKey);
        cmd.Parameters.AddWithValue("$value", enabled ? "1" : "0");
        cmd.ExecuteNonQuery();
    }
}
