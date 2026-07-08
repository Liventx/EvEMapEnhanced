using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Avalonia;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Exact per-system 2D layout coordinates as published on dotlan.evemaps.com's own region maps.
/// Extracted once (offline) from Dotlan's SVG pages and bundled as a read-only embedded resource
/// so the Schematic display mode can reproduce Dotlan's own in-region arrangement instead of a
/// generic force-directed graph drawing. (Region-to-region placement is derived from each region's
/// real in-game centroid instead -- see <see cref="SchematicMapLayout"/> -- so the retired
/// per-region universe-overview data set is no longer wired in.)
/// </summary>
internal static class DotlanLayoutData
{
    private const string SystemPositionsResourceName = "EvEMapEnhanced.Desktop.DotlanLayout.dotlan-positions.json";

    private static readonly Lazy<IReadOnlyDictionary<int, Point>> LazyPositions = new(() => LoadPoints(SystemPositionsResourceName));

    /// <summary>Solar system id -> Dotlan's own local (x, y) pixel position within its region's SVG canvas.</summary>
    public static IReadOnlyDictionary<int, Point> Positions => LazyPositions.Value;

    private static IReadOnlyDictionary<int, Point> LoadPoints(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return new Dictionary<int, Point>();

        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<int, Point>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out int id)) continue;
            double x = property.Value[0].GetDouble();
            double y = property.Value[1].GetDouble();
            result[id] = new Point(x, y);
        }
        return result;
    }
}
