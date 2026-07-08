using Avalonia;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Desktop;

namespace EvEMapEnhanced.Desktop.Tests;

public class SchematicMapLayoutTests
{
    /// <summary>
    /// Builds a solar system whose top-down projection (X, -Z light years) lands at the given
    /// on-screen point, so tests can place whole regions at known relative positions.
    /// </summary>
    private static SolarSystem SystemAt(int id, int regionId, double screenX, double screenY) =>
        new(
            Id: id,
            Name: $"S{id}",
            ConstellationId: regionId,
            RegionId: regionId,
            Security: 0.0,
            X: SpaceMath.LightYearsToMeters(screenX),
            Y: 0.0,
            Z: SpaceMath.LightYearsToMeters(-screenY));

    /// <summary>A small 4-system cluster centered on the given screen point, chained by gates.</summary>
    private static (List<SolarSystem> Systems, List<Stargate> Gates) Cluster(
        int regionId, double centerX, double centerY, double spread)
    {
        int baseId = regionId * 100;
        var systems = new List<SolarSystem>
        {
            SystemAt(baseId + 0, regionId, centerX - spread, centerY - spread),
            SystemAt(baseId + 1, regionId, centerX + spread, centerY - spread),
            SystemAt(baseId + 2, regionId, centerX + spread, centerY + spread),
            SystemAt(baseId + 3, regionId, centerX - spread, centerY + spread),
        };
        var gates = new List<Stargate>
        {
            new(baseId + 0, baseId + 1),
            new(baseId + 1, baseId + 2),
            new(baseId + 2, baseId + 3),
        };
        return (systems, gates);
    }

    private static Rect RegionBounds(SchematicMapLayout layout, IEnumerable<SolarSystem> systems)
    {
        var points = systems.Select(layout.GetPosition).ToList();
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
    }

    [Fact]
    public void Build_AnchorsRegionsByTheirRealCoordinateArrangement()
    {
        var west = Cluster(10, centerX: -300, centerY: 0, spread: 8);
        var east = Cluster(20, centerX: 300, centerY: 0, spread: 8);
        var south = Cluster(30, centerX: 0, centerY: 300, spread: 8);

        var systems = west.Systems.Concat(east.Systems).Concat(south.Systems).ToList();
        var gates = west.Gates.Concat(east.Gates).Concat(south.Gates).ToList();
        var map = new UniverseMap(systems, gates);

        var layout = SchematicMapLayout.Build(map, regionNames: null);

        var w = layout.RegionCentroids[10];
        var e = layout.RegionCentroids[20];
        var s = layout.RegionCentroids[30];

        // East region stays east of the west region; south region stays below both (screen +Y).
        Assert.True(e.X > w.X, "east region should stay east of the west region");
        Assert.True(s.Y > w.Y && s.Y > e.Y, "south region should stay below the east/west regions");
    }

    [Fact]
    public void Build_KeepsRegionBoundingBoxesFromOverlapping()
    {
        // Deliberately crowd the real-coordinate centroids so the raw projection would overlap,
        // forcing the uniform anchor-scale (and residual separation) to spread them apart.
        var a = Cluster(10, centerX: 0, centerY: 0, spread: 30);
        var b = Cluster(20, centerX: 40, centerY: 0, spread: 30);
        var c = Cluster(30, centerX: 20, centerY: 40, spread: 30);

        var systems = a.Systems.Concat(b.Systems).Concat(c.Systems).ToList();
        var gates = a.Gates.Concat(b.Gates).Concat(c.Gates).ToList();
        var map = new UniverseMap(systems, gates);

        var layout = SchematicMapLayout.Build(map, regionNames: null);

        var boundsA = RegionBounds(layout, a.Systems);
        var boundsB = RegionBounds(layout, b.Systems);
        var boundsC = RegionBounds(layout, c.Systems);

        Assert.False(boundsA.Intersects(boundsB), "regions A and B must not overlap");
        Assert.False(boundsA.Intersects(boundsC), "regions A and C must not overlap");
        Assert.False(boundsB.Intersects(boundsC), "regions B and C must not overlap");
    }

    [Fact]
    public void Build_UsesCuratedInGameGridWhenRegionNamesAreKnown()
    {
        // Real coordinates are deliberately "wrong" (all clustered) so that only the curated
        // in-game grid (looked up by region name) can produce the correct arrangement.
        var forge = Cluster(1, centerX: 0, centerY: 0, spread: 6);
        var delve = Cluster(2, centerX: 3, centerY: 0, spread: 6);
        var cobalt = Cluster(3, centerX: 0, centerY: 3, spread: 6);
        var paragon = Cluster(4, centerX: 3, centerY: 3, spread: 6);

        var systems = forge.Systems.Concat(delve.Systems).Concat(cobalt.Systems).Concat(paragon.Systems).ToList();
        var gates = forge.Gates.Concat(delve.Gates).Concat(cobalt.Gates).Concat(paragon.Gates).ToList();
        var map = new UniverseMap(systems, gates);

        var regionNames = new Dictionary<int, string>
        {
            [1] = "The Forge",
            [2] = "Delve",
            [3] = "Cobalt Edge",
            [4] = "Paragon Soul",
        };

        var layout = SchematicMapLayout.Build(map, regionNames);

        var f = layout.RegionCentroids[1];
        var d = layout.RegionCentroids[2];
        var c = layout.RegionCentroids[3];
        var p = layout.RegionCentroids[4];

        // In-game grid: Cobalt Edge is far east, Delve is west; Paragon Soul is the far south.
        Assert.True(c.X > f.X, "Cobalt Edge should sit east of The Forge");
        Assert.True(f.X > d.X, "The Forge should sit east of Delve");
        Assert.True(d.Y > f.Y, "Delve should sit south of The Forge");
        Assert.True(p.Y > d.Y, "Paragon Soul should be the southernmost");
    }

    [Fact]
    public void Build_ScalesCrowdedAnchorsApartWhilePreservingOrder()
    {
        // Two regions closer together (in real coords) than their footprints need: the uniform
        // anchor scale must push their centroids farther apart than the raw 40-unit spacing.
        var left = Cluster(10, centerX: 0, centerY: 0, spread: 25);
        var right = Cluster(20, centerX: 40, centerY: 0, spread: 25);

        var systems = left.Systems.Concat(right.Systems).ToList();
        var gates = left.Gates.Concat(right.Gates).ToList();
        var map = new UniverseMap(systems, gates);

        var layout = SchematicMapLayout.Build(map, regionNames: null);

        var l = layout.RegionCentroids[10];
        var r = layout.RegionCentroids[20];

        Assert.True(r.X > l.X, "ordering must be preserved after scaling");
        Assert.True(r.X - l.X > 40, "crowded anchors must be scaled farther apart than their raw spacing");
    }
}
