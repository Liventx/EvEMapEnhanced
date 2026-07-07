using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using Xunit;

namespace EvEMapEnhanced.Core.Tests.Routing;

public class PilotSpaceFilterTests
{
    private static SolarSystem System(int id, int regionId) => new(id, $"Sys{id}", 1, regionId, 0.5, id, 0, 0);

    [Fact]
    public void KeepsLargeGateConnectedKSpaceCluster()
    {
        // 12 systems in a chain, all in a normal k-space region -> should all survive.
        var systems = Enumerable.Range(1, 12).Select(i => System(i, regionId: 10000001)).ToList();
        var gates = Enumerable.Range(1, 11).Select(i => new Stargate(i, i + 1)).ToList();

        var result = PilotSpaceFilter.FilterToAccessibleSystems(systems, gates);

        Assert.Equal(12, result.Count);
    }

    [Fact]
    public void DropsSmallDisconnectedClusters_EvenInNormalRegionIdRange()
    {
        // A legitimate 12-system main cluster, plus a 3-system island (no gates) that
        // happens to carry a "normal" region id -- mimics CCP's internal test regions.
        var mainCluster = Enumerable.Range(1, 12).Select(i => System(i, regionId: 10000001)).ToList();
        var mainGates = Enumerable.Range(1, 11).Select(i => new Stargate(i, i + 1)).ToList();

        var island = Enumerable.Range(101, 3).Select(i => System(i, regionId: 10000002)).ToList();
        // no gates for the island at all

        var result = PilotSpaceFilter.FilterToAccessibleSystems(
            mainCluster.Concat(island).ToList(), mainGates);

        Assert.Equal(12, result.Count);
        Assert.DoesNotContain(result, s => s.Id >= 101);
    }

    [Fact]
    public void KeepsMultipleDisconnectedClusters_WhenBothAreLargeEnough()
    {
        // Mirrors real New Eden: the main cluster and a legitimate but gate-isolated
        // pocket like Pochven (reachable only via jump drive/filaments, not stargates)
        // must both survive -- being disconnected from the main cluster is not itself
        // a reason to drop a region, only being a tiny/junk island is.
        var mainCluster = Enumerable.Range(1, 12).Select(i => System(i, regionId: 10000001)).ToList();
        var mainGates = Enumerable.Range(1, 11).Select(i => new Stargate(i, i + 1)).ToList();

        var pochvenLike = Enumerable.Range(101, 15).Select(i => System(i, regionId: 10000070)).ToList();
        var pochvenGates = Enumerable.Range(101, 14).Select(i => new Stargate(i, i + 1)).ToList();

        var result = PilotSpaceFilter.FilterToAccessibleSystems(
            mainCluster.Concat(pochvenLike).ToList(), mainGates.Concat(pochvenGates).ToList());

        Assert.Equal(27, result.Count);
        Assert.Contains(result, s => s.RegionId == 10000070);
    }

    [Fact]
    public void DropsWormholeAndAbyssalRegions_RegardlessOfSize()
    {
        var mainCluster = Enumerable.Range(1, 12).Select(i => System(i, regionId: 10000001)).ToList();
        var mainGates = Enumerable.Range(1, 11).Select(i => new Stargate(i, i + 1)).ToList();

        // A large, fully gate-connected wormhole "region" -- still must be excluded by region id.
        var wormholeCluster = Enumerable.Range(201, 20).Select(i => System(i, regionId: 11000005)).ToList();
        var wormholeGates = Enumerable.Range(201, 19).Select(i => new Stargate(i, i + 1)).ToList();

        var abyssalCluster = Enumerable.Range(301, 20).Select(i => System(i, regionId: 12000001)).ToList();

        var result = PilotSpaceFilter.FilterToAccessibleSystems(
            mainCluster.Concat(wormholeCluster).Concat(abyssalCluster).ToList(),
            mainGates.Concat(wormholeGates).ToList());

        Assert.Equal(12, result.Count);
        Assert.All(result, s => Assert.True(s.Id <= 12));
    }
}
