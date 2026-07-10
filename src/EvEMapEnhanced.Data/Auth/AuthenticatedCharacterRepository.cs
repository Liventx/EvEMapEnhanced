using System.Globalization;
using EvEMapEnhanced.Core.Auth;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Data.User;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>CRUD persistence for <see cref="AuthenticatedCharacter"/> records, with the refresh token encrypted at rest.</summary>
public sealed class AuthenticatedCharacterRepository
{
    private readonly string _sqlitePath;

    public AuthenticatedCharacterRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public IReadOnlyList<AuthenticatedCharacter> LoadAll()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT CharacterId, Name, JumpDriveCalibration, JumpFuelConservation, JumpFreighters,
                   CapitalShips, BlackOps, SkillsUpdatedUtc, LastKnownSystemId
            FROM AuthenticatedCharacters
            ORDER BY Name;
            """;

        var result = new List<AuthenticatedCharacter>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AuthenticatedCharacter
            {
                CharacterId = reader.GetInt64(0),
                Name = reader.GetString(1),
                Skills = new PilotSkills
                {
                    JumpDriveCalibration = reader.GetInt32(2),
                    JumpFuelConservation = reader.GetInt32(3),
                    JumpFreighters = reader.GetInt32(4),
                    CapitalShips = reader.GetInt32(5),
                    BlackOps = reader.GetInt32(6),
                },
                SkillsUpdatedUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LastKnownSystemId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            });
        }
        return result;
    }

    /// <summary>Inserts a newly signed-in character, or updates its stored token/scopes if it already exists.</summary>
    public void Upsert(long characterId, string name, string refreshToken, IEnumerable<string> scopes)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuthenticatedCharacters (CharacterId, Name, RefreshTokenProtected, Scopes, AddedAtUtc)
            VALUES ($id, $name, $token, $scopes, $added)
            ON CONFLICT(CharacterId) DO UPDATE SET
                Name = excluded.Name,
                RefreshTokenProtected = excluded.RefreshTokenProtected,
                Scopes = excluded.Scopes;
            """;
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$token", TokenProtector.Protect(refreshToken));
        cmd.Parameters.AddWithValue("$scopes", string.Join(' ', scopes));
        cmd.Parameters.AddWithValue("$added", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public string? GetRefreshToken(long characterId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT RefreshTokenProtected FROM AuthenticatedCharacters WHERE CharacterId = $id;";
        cmd.Parameters.AddWithValue("$id", characterId);
        return cmd.ExecuteScalar() is byte[] protectedToken ? TokenProtector.Unprotect(protectedToken) : null;
    }

    public IReadOnlySet<string> GetGrantedScopes(long characterId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Scopes FROM AuthenticatedCharacters WHERE CharacterId = $id;";
        cmd.Parameters.AddWithValue("$id", characterId);
        if (cmd.ExecuteScalar() is not string scopes || string.IsNullOrWhiteSpace(scopes))
            return new HashSet<string>(StringComparer.Ordinal);

        return scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool HasScope(long characterId, string scope) =>
        GetGrantedScopes(characterId).Contains(scope);

    public void UpdateRefreshToken(long characterId, string refreshToken)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE AuthenticatedCharacters SET RefreshTokenProtected = $token WHERE CharacterId = $id;";
        cmd.Parameters.AddWithValue("$token", TokenProtector.Protect(refreshToken));
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSkills(long characterId, PilotSkills skills)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE AuthenticatedCharacters SET
                JumpDriveCalibration = $jdc, JumpFuelConservation = $jfc, JumpFreighters = $jf,
                CapitalShips = $cap, BlackOps = $bo, SkillsUpdatedUtc = $updated
            WHERE CharacterId = $id;
            """;
        cmd.Parameters.AddWithValue("$jdc", skills.JumpDriveCalibration);
        cmd.Parameters.AddWithValue("$jfc", skills.JumpFuelConservation);
        cmd.Parameters.AddWithValue("$jf", skills.JumpFreighters);
        cmd.Parameters.AddWithValue("$cap", skills.CapitalShips);
        cmd.Parameters.AddWithValue("$bo", skills.BlackOps);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateLocation(long characterId, int? systemId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE AuthenticatedCharacters SET LastKnownSystemId = $sys WHERE CharacterId = $id;";
        cmd.Parameters.AddWithValue("$sys", (object?)systemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long characterId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var transaction = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM AuthenticatedCharacters WHERE CharacterId = $id;";
            cmd.Parameters.AddWithValue("$id", characterId);
            cmd.ExecuteNonQuery();
        }

        // Clear the "last active pilot" pointer if it was this character, so a future launch
        // doesn't try to restore a selection that no longer has stored credentials.
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM AppSettings WHERE Key = $key AND Value = $id;";
            cmd.Parameters.AddWithValue("$key", ActiveCharacterSettingKey);
            cmd.Parameters.AddWithValue("$id", characterId.ToString(CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        var remainingCynoIds = ReadActiveCynoCharacterIds(connection, transaction)
            .Where(id => id != characterId)
            .ToList();
        WriteActiveCynoCharacterIds(connection, remainingCynoIds, transaction);

        var remainingScIds = ReadActiveScCharacterIds(connection, transaction)
            .Where(id => id != characterId)
            .ToList();
        WriteActiveScCharacterIds(connection, remainingScIds, transaction);

        transaction.Commit();
    }

    private const string ActiveCharacterSettingKey = "ActiveCharacterId";
    private const string ActiveCynoCharacterSettingKey = "ActiveCynoCharacterId";
    private const string ActiveScCharacterSettingKey = "ActiveScCharacterId";

    /// <summary>
    /// The character last selected as the active pilot (see <see cref="SetActiveCharacterId"/>),
    /// so the app can restore that selection on the next launch instead of defaulting to "no
    /// pilot" and forcing the user to re-pick (or re-sign-in) every session.
    /// </summary>
    public long? GetActiveCharacterId()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ActiveCharacterSettingKey);
        return cmd.ExecuteScalar() is string value && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
            ? id
            : null;
    }

    /// <summary>Persists which character is the active pilot; pass null to clear it ("no pilot").</summary>
    public void SetActiveCharacterId(long? characterId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        if (characterId is null)
        {
            cmd.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
            cmd.Parameters.AddWithValue("$key", ActiveCharacterSettingKey);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            cmd.Parameters.AddWithValue("$key", ActiveCharacterSettingKey);
            cmd.Parameters.AddWithValue("$value", characterId.Value.ToString(CultureInfo.InvariantCulture));
        }
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Characters selected as tracked cyno pilots (see <see cref="SetActiveCynoCharacterIds"/>).
    /// Stored as a comma-separated list for backward compatibility with older single-selection saves.
    /// </summary>
    public IReadOnlyList<long> GetActiveCynoCharacterIds()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        return ReadActiveCynoCharacterIds(connection);
    }

    /// <summary>Persists the tracked cyno pilots; pass an empty collection to clear them all.</summary>
    public void SetActiveCynoCharacterIds(IReadOnlyCollection<long> characterIds)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        WriteActiveCynoCharacterIds(connection, characterIds);
    }

    private static IReadOnlyList<long> ReadActiveCynoCharacterIds(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ActiveCynoCharacterSettingKey);
        if (cmd.ExecuteScalar() is not string value || string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<long>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
    }

    private static void WriteActiveCynoCharacterIds(
        SqliteConnection connection,
        IReadOnlyCollection<long> characterIds,
        SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        if (characterIds.Count == 0)
        {
            cmd.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
            cmd.Parameters.AddWithValue("$key", ActiveCynoCharacterSettingKey);
        }
        else
        {
            var value = string.Join(',', characterIds.Distinct().OrderBy(id => id));
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            cmd.Parameters.AddWithValue("$key", ActiveCynoCharacterSettingKey);
            cmd.Parameters.AddWithValue("$value", value);
        }
        cmd.ExecuteNonQuery();
    }

    /// <summary>Backward-compatible single-selection accessor; returns the first tracked cyno pilot, if any.</summary>
    public long? GetActiveCynoCharacterId() =>
        GetActiveCynoCharacterIds().FirstOrDefault() is long id ? id : null;

    /// <summary>Backward-compatible single-selection setter; replaces the whole cyno selection with one ID.</summary>
    public void SetActiveCynoCharacterId(long? characterId) =>
        SetActiveCynoCharacterIds(characterId is long id ? new[] { id } : Array.Empty<long>());

    /// <summary>
    /// Characters selected as tracked SC pilots (see <see cref="SetActiveScCharacterIds"/>).
    /// Stored as a comma-separated list.
    /// </summary>
    public IReadOnlyList<long> GetActiveScCharacterIds()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        return ReadActiveScCharacterIds(connection);
    }

    /// <summary>Persists the tracked SC pilots; pass an empty collection to clear them all.</summary>
    public void SetActiveScCharacterIds(IReadOnlyCollection<long> characterIds)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        WriteActiveScCharacterIds(connection, characterIds);
    }

    private static IReadOnlyList<long> ReadActiveScCharacterIds(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", ActiveScCharacterSettingKey);
        if (cmd.ExecuteScalar() is not string value || string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<long>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
    }

    private static void WriteActiveScCharacterIds(
        SqliteConnection connection,
        IReadOnlyCollection<long> characterIds,
        SqliteTransaction? transaction = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        if (characterIds.Count == 0)
        {
            cmd.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
            cmd.Parameters.AddWithValue("$key", ActiveScCharacterSettingKey);
        }
        else
        {
            var value = string.Join(',', characterIds.Distinct().OrderBy(id => id));
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            cmd.Parameters.AddWithValue("$key", ActiveScCharacterSettingKey);
            cmd.Parameters.AddWithValue("$value", value);
        }
        cmd.ExecuteNonQuery();
    }
}
