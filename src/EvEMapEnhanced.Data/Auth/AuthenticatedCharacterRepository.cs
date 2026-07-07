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
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AuthenticatedCharacters WHERE CharacterId = $id;";
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }
}
