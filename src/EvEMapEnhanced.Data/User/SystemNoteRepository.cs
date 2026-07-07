using System.Text.Json;
using EvEMapEnhanced.Core.Notes;

namespace EvEMapEnhanced.Data.User;

/// <summary>CRUD persistence for per-system user notes.</summary>
public sealed class SystemNoteRepository
{
    private readonly string _sqlitePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SystemNoteRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public SystemNote? Get(int solarSystemId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Text, TagsJson FROM SystemNotes WHERE SolarSystemId = $id;";
        cmd.Parameters.AddWithValue("$id", solarSystemId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new SystemNote
        {
            SolarSystemId = solarSystemId,
            Text = reader.GetString(0),
            Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(1), JsonOptions) ?? new(),
        };
    }

    public IReadOnlyDictionary<int, SystemNote> LoadAll()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SolarSystemId, Text, TagsJson FROM SystemNotes;";
        using var reader = cmd.ExecuteReader();

        var dict = new Dictionary<int, SystemNote>();
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            dict[id] = new SystemNote
            {
                SolarSystemId = id,
                Text = reader.GetString(1),
                Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), JsonOptions) ?? new(),
            };
        }
        return dict;
    }

    public void Save(SystemNote note)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SystemNotes (SolarSystemId, Text, TagsJson) VALUES ($id, $text, $tags)
            ON CONFLICT(SolarSystemId) DO UPDATE SET Text = excluded.Text, TagsJson = excluded.TagsJson;
            """;
        cmd.Parameters.AddWithValue("$id", note.SolarSystemId);
        cmd.Parameters.AddWithValue("$text", note.Text);
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(note.Tags, JsonOptions));
        cmd.ExecuteNonQuery();
    }

    public void Delete(int solarSystemId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SystemNotes WHERE SolarSystemId = $id;";
        cmd.Parameters.AddWithValue("$id", solarSystemId);
        cmd.ExecuteNonQuery();
    }
}
