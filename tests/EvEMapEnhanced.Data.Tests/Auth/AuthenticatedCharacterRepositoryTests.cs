using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Data.Tests.Auth;

public class AuthenticatedCharacterRepositoryTests : IDisposable
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"user-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void Upsert_RoundTripsCharacterAndEncryptedRefreshToken()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);

        repo.Upsert(95465499, "Test Pilot", "refresh-token-value", new[] { "esi-skills.read_skills.v1" });

        var loaded = repo.LoadAll().Single();
        Assert.Equal(95465499, loaded.CharacterId);
        Assert.Equal("Test Pilot", loaded.Name);
        Assert.Null(loaded.LastKnownSystemId);

        Assert.Equal("refresh-token-value", repo.GetRefreshToken(95465499));
    }

    [Fact]
    public void Upsert_SameCharacterId_UpdatesInPlaceInsteadOfDuplicating()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);

        repo.Upsert(1, "Old Name", "token-1", new[] { "scope-a" });
        repo.Upsert(1, "New Name", "token-2", new[] { "scope-a", "scope-b" });

        var loaded = repo.LoadAll().Single();
        Assert.Equal("New Name", loaded.Name);
        Assert.Equal("token-2", repo.GetRefreshToken(1));
    }

    [Fact]
    public void UpdateSkills_PersistsJumpRelevantLevels()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(2, "Pilot", "token", Array.Empty<string>());

        repo.UpdateSkills(2, new Core.Jump.PilotSkills
        {
            JumpDriveCalibration = 5,
            JumpFuelConservation = 4,
            JumpFreighters = 3,
            CapitalShips = 5,
            BlackOps = 1,
        });

        var loaded = repo.LoadAll().Single();
        Assert.Equal(5, loaded.Skills.JumpDriveCalibration);
        Assert.Equal(4, loaded.Skills.JumpFuelConservation);
        Assert.Equal(3, loaded.Skills.JumpFreighters);
        Assert.Equal(5, loaded.Skills.CapitalShips);
        Assert.Equal(1, loaded.Skills.BlackOps);
        Assert.NotNull(loaded.SkillsUpdatedUtc);
    }

    [Fact]
    public void UpdateLocation_PersistsLastKnownSystem()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(3, "Pilot", "token", Array.Empty<string>());

        repo.UpdateLocation(3, 30000142);

        Assert.Equal(30000142, repo.LoadAll().Single().LastKnownSystemId);
    }

    [Fact]
    public void UpdateRefreshToken_ReplacesStoredToken()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(4, "Pilot", "token-old", Array.Empty<string>());

        repo.UpdateRefreshToken(4, "token-new");

        Assert.Equal("token-new", repo.GetRefreshToken(4));
    }

    [Fact]
    public void Delete_RemovesCharacter()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(5, "Pilot", "token", Array.Empty<string>());

        repo.Delete(5);

        Assert.Empty(repo.LoadAll());
        Assert.Null(repo.GetRefreshToken(5));
    }

    [Fact]
    public void ActiveCharacterId_RoundTripsAcrossReopenedConnections()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(6, "Pilot", "token", Array.Empty<string>());

        Assert.Null(repo.GetActiveCharacterId());

        repo.SetActiveCharacterId(6);

        // A fresh repository instance (simulating an app restart) must see the same value --
        // this is the persistence contract "sign in once, no need to re-pick after restart"
        // relies on.
        var reopened = new AuthenticatedCharacterRepository(_sqlitePath);
        Assert.Equal(6, reopened.GetActiveCharacterId());
    }

    [Fact]
    public void SetActiveCharacterId_Null_ClearsSelection()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(7, "Pilot", "token", Array.Empty<string>());
        repo.SetActiveCharacterId(7);

        repo.SetActiveCharacterId(null);

        Assert.Null(repo.GetActiveCharacterId());
    }

    [Fact]
    public void SetActiveCharacterId_OverwritesPreviousSelection()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(8, "Pilot A", "token-a", Array.Empty<string>());
        repo.Upsert(9, "Pilot B", "token-b", Array.Empty<string>());

        repo.SetActiveCharacterId(8);
        repo.SetActiveCharacterId(9);

        Assert.Equal(9, repo.GetActiveCharacterId());
    }

    [Fact]
    public void Delete_ClearsActiveCharacterIdIfItWasTheDeletedCharacter()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(10, "Pilot", "token", Array.Empty<string>());
        repo.SetActiveCharacterId(10);

        repo.Delete(10);

        Assert.Null(repo.GetActiveCharacterId());
    }

    [Fact]
    public void Delete_LeavesActiveCharacterIdUntouchedWhenADifferentCharacterIsDeleted()
    {
        var repo = new AuthenticatedCharacterRepository(_sqlitePath);
        repo.Upsert(11, "Active Pilot", "token-active", Array.Empty<string>());
        repo.Upsert(12, "Other Pilot", "token-other", Array.Empty<string>());
        repo.SetActiveCharacterId(11);

        repo.Delete(12);

        Assert.Equal(11, repo.GetActiveCharacterId());
    }

    public void Dispose()
    {
        try { if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath); } catch { }
    }
}
