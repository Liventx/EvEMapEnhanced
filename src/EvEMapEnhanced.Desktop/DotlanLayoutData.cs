using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Avalonia;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Exact 2D layout coordinates as published on dotlan.evemaps.com's own maps. Extracted once
/// (offline) from Dotlan's SVG pages and bundled as read-only embedded resources so the
/// Schematic display mode can reproduce Dotlan's own arrangement instead of a generic
/// force-directed graph drawing, at two levels:
/// <list type="bullet">
/// <item><see cref="Positions"/> -- per-system position inside its region's own SVG canvas
/// (from each region's individual map page).</item>
/// <item><see cref="RegionPositions"/> -- per-region position on Dotlan's universe overview
/// map (from evemaps.dotlan.net's "Universe" page world database), used to arrange whole
/// regions relative to each other the same way Dotlan does.</item>
/// </list>
/// </summary>
internal static class DotlanLayoutData
{
    private const string SystemPositionsResourceName = "EvEMapEnhanced.Desktop.DotlanLayout.dotlan-positions.json";
    private const string RegionPositionsResourceName = "EvEMapEnhanced.Desktop.DotlanLayout.dotlan-region-positions.json";

    private static readonly Lazy<IReadOnlyDictionary<int, Point>> LazyPositions = new(() => LoadPoints(SystemPositionsResourceName));
    private static readonly Lazy<IReadOnlyDictionary<int, Point>> LazyRegionPositions = new(() => LoadPoints(RegionPositionsResourceName));

    /// <summary>Solar system id -> Dotlan's own local (x, y) pixel position within its region's SVG canvas.</summary>
    public static IReadOnlyDictionary<int, Point> Positions => LazyPositions.Value;

    /// <summary>Region id -> Dotlan's own (x, y) position on its universe overview map.</summary>
    public static IReadOnlyDictionary<int, Point> RegionPositions => LazyRegionPositions.Value;

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
