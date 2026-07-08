using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class PvPActivityClassifierTests
{
    private static readonly KillVictimFilter Filter = new(new HashSet<int> { 670, 606 });
    private static readonly NpcCapitalKillFilter NpcCapitalFilter = new(new HashSet<int> { 19726 });

    [Fact]
    public void Classify_Hot_WhenFiveValidPlayerDeathsInHour()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = Enumerable.Range(0, 5).Select(i => new ZKillboardKillmail
        {
            SolarSystemId = 30000142,
            KillmailTime = now.AddMinutes(-10 - i).ToString("O"),
            VictimShipTypeId = 587,
            Npc = false,
        });

        var level = PvPActivityClassifier.Classify(kills, Filter, now);

        Assert.Equal(PvPActivityLevel.Hot, level);
    }

    [Fact]
    public void Classify_Recent_WhenOneToFourValidDeathsInHour()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = new[]
        {
            new ZKillboardKillmail { SolarSystemId = 30000142, KillmailTime = now.AddMinutes(-20).ToString("O"), VictimShipTypeId = 587, Npc = false },
            new ZKillboardKillmail { SolarSystemId = 30000142, KillmailTime = now.AddMinutes(-50).ToString("O"), VictimShipTypeId = 587, Npc = false },
        };

        var level = PvPActivityClassifier.Classify(kills, Filter, now);

        Assert.Equal(PvPActivityLevel.Recent, level);
    }

    [Fact]
    public void Classify_None_WhenExcludedVictimTypesOrNpc()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = new[]
        {
            new ZKillboardKillmail { SolarSystemId = 30000142, KillmailTime = now.AddMinutes(-5).ToString("O"), VictimShipTypeId = 670, Npc = false },
            new ZKillboardKillmail { SolarSystemId = 30000142, KillmailTime = now.AddMinutes(-6).ToString("O"), VictimShipTypeId = 587, Npc = true },
        };

        var level = PvPActivityClassifier.Classify(kills, Filter, now, NpcCapitalFilter);

        Assert.Equal(PvPActivityLevel.None, level);
    }

    [Fact]
    public void Classify_NpcCapital_WhenNpcDreadVictimWithinThirtyMinutes()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = new[]
        {
            new ZKillboardKillmail
            {
                SolarSystemId = 30000142,
                KillmailTime = now.AddMinutes(-15).ToString("O"),
                VictimShipTypeId = 19726,
                Npc = true,
            },
        };

        var level = PvPActivityClassifier.Classify(kills, Filter, now, NpcCapitalFilter);

        Assert.Equal(PvPActivityLevel.NpcCapital, level);
    }

    [Fact]
    public void Classify_NpcCapital_WhenNpcDreadAttackerWithinThirtyMinutes()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = new[]
        {
            new ZKillboardKillmail
            {
                SolarSystemId = 30000142,
                KillmailTime = now.AddMinutes(-10).ToString("O"),
                VictimShipTypeId = 587,
                Npc = true,
                AttackerShipTypeIds = new[] { 19726 },
            },
        };

        var level = PvPActivityClassifier.Classify(kills, Filter, now, NpcCapitalFilter);

        Assert.Equal(PvPActivityLevel.NpcCapital, level);
    }

    [Fact]
    public void Classify_NpcCapital_TakesPriorityOverHot()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = Enumerable.Range(0, 5).Select(i => new ZKillboardKillmail
        {
            SolarSystemId = 30000142,
            KillmailTime = now.AddMinutes(-5 - i).ToString("O"),
            VictimShipTypeId = 587,
            Npc = false,
        }).Append(new ZKillboardKillmail
        {
            SolarSystemId = 30000142,
            KillmailTime = now.AddMinutes(-8).ToString("O"),
            VictimShipTypeId = 19726,
            Npc = true,
        });

        var level = PvPActivityClassifier.Classify(kills, Filter, now, NpcCapitalFilter);

        Assert.Equal(PvPActivityLevel.NpcCapital, level);
    }

    [Fact]
    public void Classify_None_WhenNpcCapitalOutsideThirtyMinutes()
    {
        var now = DateTime.Parse("2026-07-08T12:00:00Z");
        var kills = new[]
        {
            new ZKillboardKillmail
            {
                SolarSystemId = 30000142,
                KillmailTime = now.AddMinutes(-40).ToString("O"),
                VictimShipTypeId = 19726,
                Npc = true,
            },
        };

        var level = PvPActivityClassifier.Classify(kills, Filter, now, NpcCapitalFilter);

        Assert.Equal(PvPActivityLevel.None, level);
    }
}
