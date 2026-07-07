using EvEMapEnhanced.Core.Jump;
using Microsoft.Data.Sqlite;

namespace EvEMapEnhanced.Data.User;

/// <summary>CRUD persistence for <see cref="PilotProfile"/> records, including editable skills and avoid lists.</summary>
public sealed class PilotProfileRepository
{
    private readonly string _sqlitePath;

    public PilotProfileRepository(string sqlitePath) => _sqlitePath = sqlitePath;

    public IReadOnlyList<PilotProfile> LoadAll()
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, JumpDriveCalibration, JumpFuelConservation, JumpFreighters,
                   CapitalShips, BlackOps, Economizer, AvoidLowSec, AvoidNullSec, AvoidRecentKillActivity
            FROM PilotProfiles;
            """;

        var profiles = new List<PilotProfile>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                profiles.Add(new PilotProfile
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Skills = new PilotSkills
                    {
                        JumpDriveCalibration = reader.GetInt32(2),
                        JumpFuelConservation = reader.GetInt32(3),
                        JumpFreighters = reader.GetInt32(4),
                        CapitalShips = reader.GetInt32(5),
                        BlackOps = reader.GetInt32(6),
                        Economizer = Enum.Parse<JumpDriveEconomizerTier>(reader.GetString(7)),
                    },
                    AvoidLowSec = reader.GetInt32(8) != 0,
                    AvoidNullSec = reader.GetInt32(9) != 0,
                    AvoidRecentKillActivity = reader.GetInt32(10) != 0,
                });
            }
        }

        foreach (var profile in profiles)
        {
            profile.AvoidSystemIds = LoadAvoidSystems(connection, profile.Id);
        }

        return profiles;
    }

    private static HashSet<int> LoadAvoidSystems(SqliteConnection connection, int profileId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SystemId FROM PilotProfileAvoidSystems WHERE ProfileId = $id;";
        cmd.Parameters.AddWithValue("$id", profileId);
        var set = new HashSet<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) set.Add(reader.GetInt32(0));
        return set;
    }

    public int Save(PilotProfile profile)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var transaction = connection.BeginTransaction();

        if (profile.Id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO PilotProfiles
                    (Name, JumpDriveCalibration, JumpFuelConservation, JumpFreighters, CapitalShips, BlackOps, Economizer, AvoidLowSec, AvoidNullSec, AvoidRecentKillActivity)
                VALUES
                    ($name, $jdc, $jfc, $jf, $cap, $bo, $eco, $alow, $anull, $akill);
                SELECT last_insert_rowid();
                """;
            BindProfile(insert, profile);
            profile.Id = Convert.ToInt32((long)insert.ExecuteScalar()!);
        }
        else
        {
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE PilotProfiles SET
                    Name = $name, JumpDriveCalibration = $jdc, JumpFuelConservation = $jfc,
                    JumpFreighters = $jf, CapitalShips = $cap, BlackOps = $bo, Economizer = $eco,
                    AvoidLowSec = $alow, AvoidNullSec = $anull, AvoidRecentKillActivity = $akill
                WHERE Id = $id;
                """;
            BindProfile(update, profile);
            update.Parameters.AddWithValue("$id", profile.Id);
            update.ExecuteNonQuery();
        }

        using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM PilotProfileAvoidSystems WHERE ProfileId = $id;";
            clear.Parameters.AddWithValue("$id", profile.Id);
            clear.ExecuteNonQuery();
        }

        foreach (int systemId in profile.AvoidSystemIds)
        {
            using var insertAvoid = connection.CreateCommand();
            insertAvoid.CommandText = "INSERT INTO PilotProfileAvoidSystems (ProfileId, SystemId) VALUES ($pid, $sid);";
            insertAvoid.Parameters.AddWithValue("$pid", profile.Id);
            insertAvoid.Parameters.AddWithValue("$sid", systemId);
            insertAvoid.ExecuteNonQuery();
        }

        transaction.Commit();
        return profile.Id;
    }

    private static void BindProfile(SqliteCommand cmd, PilotProfile profile)
    {
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$jdc", profile.Skills.JumpDriveCalibration);
        cmd.Parameters.AddWithValue("$jfc", profile.Skills.JumpFuelConservation);
        cmd.Parameters.AddWithValue("$jf", profile.Skills.JumpFreighters);
        cmd.Parameters.AddWithValue("$cap", profile.Skills.CapitalShips);
        cmd.Parameters.AddWithValue("$bo", profile.Skills.BlackOps);
        cmd.Parameters.AddWithValue("$eco", profile.Skills.Economizer.ToString());
        cmd.Parameters.AddWithValue("$alow", profile.AvoidLowSec ? 1 : 0);
        cmd.Parameters.AddWithValue("$anull", profile.AvoidNullSec ? 1 : 0);
        cmd.Parameters.AddWithValue("$akill", profile.AvoidRecentKillActivity ? 1 : 0);
    }

    public void Delete(int profileId)
    {
        using var connection = UserDatabase.OpenConnection(_sqlitePath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PilotProfiles WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", profileId);
        cmd.ExecuteNonQuery();
    }
}
