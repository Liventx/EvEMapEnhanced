using System.Text.Json;
using EvEMapEnhanced.Core.Routing;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>CRUD persistence for user-saved/favorite routes.</summary>
public sealed class SavedRouteRepository
{
    private readonly string _sqlitePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SavedRouteRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public IReadOnlyList<SavedRoute> LoadAll()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, CreatedAtUtc, StepsJson FROM SavedRoutes ORDER BY CreatedAtUtc DESC;";
        using var reader = cmd.ExecuteReader();

        var list = new List<SavedRoute>();
        while (reader.Read())
        {
            list.Add(new SavedRoute
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CreatedAtUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                Steps = JsonSerializer.Deserialize<List<RouteStep>>(reader.GetString(3), JsonOptions) ?? new(),
            });
        }
        return list;
    }

    public int Save(SavedRoute route)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        string stepsJson = JsonSerializer.Serialize(route.Steps, JsonOptions);

        if (route.Id == 0)
        {
            cmd.CommandText = "INSERT INTO SavedRoutes (Name, CreatedAtUtc, StepsJson) VALUES ($name, $created, $steps); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$name", route.Name);
            cmd.Parameters.AddWithValue("$created", route.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$steps", stepsJson);
            route.Id = Convert.ToInt32((long)cmd.ExecuteScalar()!);
        }
        else
        {
            cmd.CommandText = "UPDATE SavedRoutes SET Name = $name, StepsJson = $steps WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$name", route.Name);
            cmd.Parameters.AddWithValue("$steps", stepsJson);
            cmd.Parameters.AddWithValue("$id", route.Id);
            cmd.ExecuteNonQuery();
        }
        return route.Id;
    }

    public void Delete(int id)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SavedRoutes WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
