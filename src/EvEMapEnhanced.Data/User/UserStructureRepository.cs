using EvEMapEnhanced.Core.Structures;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>CRUD persistence for user-entered structures (citadels, beacons, jammers, jump bridges).</summary>
public sealed class UserStructureRepository
{
    private readonly string _sqlitePath;

    public UserStructureRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public IReadOnlyList<UserStructure> LoadAll()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SolarSystemId, Kind, Name, OwnerTag, Access, LinkedSystemId, StrontHours, Notes
            FROM UserStructures;
            """;
        using var reader = cmd.ExecuteReader();

        var list = new List<UserStructure>();
        while (reader.Read())
        {
            list.Add(new UserStructure
            {
                Id = reader.GetInt32(0),
                SolarSystemId = reader.GetInt32(1),
                Kind = Enum.Parse<StructureKind>(reader.GetString(2)),
                Name = reader.GetString(3),
                OwnerTag = reader.IsDBNull(4) ? null : reader.GetString(4),
                Access = Enum.Parse<StructureAccessLevel>(reader.GetString(5)),
                LinkedSystemId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StrontHours = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }
        return list;
    }

    public int Save(UserStructure structure)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();

        if (structure.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO UserStructures (SolarSystemId, Kind, Name, OwnerTag, Access, LinkedSystemId, StrontHours, Notes)
                VALUES ($sys, $kind, $name, $owner, $access, $linked, $stront, $notes);
                SELECT last_insert_rowid();
                """;
            Bind(cmd, structure);
            structure.Id = Convert.ToInt32((long)cmd.ExecuteScalar()!);
        }
        else
        {
            cmd.CommandText = """
                UPDATE UserStructures SET
                    SolarSystemId = $sys, Kind = $kind, Name = $name, OwnerTag = $owner,
                    Access = $access, LinkedSystemId = $linked, StrontHours = $stront, Notes = $notes
                WHERE Id = $id;
                """;
            Bind(cmd, structure);
            cmd.Parameters.AddWithValue("$id", structure.Id);
            cmd.ExecuteNonQuery();
        }

        return structure.Id;
    }

    private static void Bind(SqliteCommand cmd, UserStructure s)
    {
        cmd.Parameters.AddWithValue("$sys", s.SolarSystemId);
        cmd.Parameters.AddWithValue("$kind", s.Kind.ToString());
        cmd.Parameters.AddWithValue("$name", s.Name);
        cmd.Parameters.AddWithValue("$owner", (object?)s.OwnerTag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$access", s.Access.ToString());
        cmd.Parameters.AddWithValue("$linked", (object?)s.LinkedSystemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stront", (object?)s.StrontHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)s.Notes ?? DBNull.Value);
    }

    public void Delete(int id)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM UserStructures WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
