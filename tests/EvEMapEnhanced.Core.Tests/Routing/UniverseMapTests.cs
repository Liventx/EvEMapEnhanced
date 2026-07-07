using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Structures;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class UniverseMapTests
{
    [Fact]
    public void SystemsWithinRange_ReturnsOnlySystemsInsideRadius()
    {
        var map = TestFixtures.BuildJumpOnlyMap();
        var origin = map.Get(101)!; // J0 at 0 LY

        var within3Ly = map.SystemsWithinRange(origin, 3.0).Select(r => r.System.Id).OrderBy(x => x).ToList();

        // J1 (2 LY away) and Jammed (~2.24 LY away) should be included; J2 (4 LY) and J3 (6 LY) excluded.
        Assert.Contains(102, within3Ly);
        Assert.Contains(105, within3Ly);
        Assert.DoesNotContain(103, within3Ly);
        Assert.DoesNotContain(104, within3Ly);
    }

    [Fact]
    public void GateNeighbors_ReturnsBidirectionalAdjacency()
    {
        var map = TestFixtures.BuildLinearGateMap();
        Assert.Contains(2, map.GateNeighbors(1));
        Assert.Contains(1, map.GateNeighbors(2));
    }

    [Fact]
    public void FindByName_IsCaseInsensitive()
    {
        var map = TestFixtures.BuildLinearGateMap();
        Assert.NotNull(map.FindByName("alpha"));
        Assert.NotNull(map.FindByName("ALPHA"));
    }

    [Fact]
    public void LoadStructures_BuildsBidirectionalJumpBridgeEdges()
    {
        var map = TestFixtures.BuildLinearGateMap();
        map.LoadStructures(new[]
        {
            new UserStructure { SolarSystemId = 1, Kind = StructureKind.Ansiblex, Name = "Bridge A", LinkedSystemId = 5 },
        });

        var neighborsOf1 = map.JumpBridgeNeighbors(1);
        var neighborsOf5 = map.JumpBridgeNeighbors(5);

        Assert.Single(neighborsOf1);
        Assert.Equal(5, neighborsOf1[0].ToSystemId);
        Assert.Single(neighborsOf5);
        Assert.Equal(1, neighborsOf5[0].ToSystemId);
    }

    [Fact]
    public void LoadStructures_TracksCynoJammedSystems()
    {
        var map = TestFixtures.BuildLinearGateMap();
        map.LoadStructures(new[]
        {
            new UserStructure { SolarSystemId = 3, Kind = StructureKind.CynoJammer, Name = "Jammer" },
        });

        Assert.True(map.IsCynoJammed(3));
        Assert.False(map.IsCynoJammed(1));
    }
}
