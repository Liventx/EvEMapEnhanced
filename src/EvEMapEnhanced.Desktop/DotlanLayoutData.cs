using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Avalonia;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// Bundled read-only 2D layout coordinates for the Schematic display mode, at two levels:
/// <list type="bullet">
/// <item><see cref="Positions"/> -- exact per-system pixel positions as published on
/// dotlan.evemaps.com's own region maps (extracted offline from Dotlan's SVG pages), so a region's
/// internal arrangement reproduces Dotlan's instead of a generic force-directed drawing.</item>
/// <item><see cref="IngameRegionPositions"/> -- a curated region-to-region anchor grid read off
/// EVE's own in-game New Eden star map, keyed by region name, so whole regions are composed the way
/// the in-game map arranges them (see <see cref="SchematicMapLayout"/>).</item>
/// </list>
/// </summary>
internal static class DotlanLayoutData
{
    private const string SystemPositionsResourceName = "EvEMapEnhanced.Desktop.DotlanLayout.dotlan-positions.json";
    private const string IngameRegionPositionsResourceName = "EvEMapEnhanced.Desktop.DotlanLayout.ingame-region-positions.json";

    private static readonly Lazy<IReadOnlyDictionary<int, Point>> LazyPositions = new(() => LoadPoints(SystemPositionsResourceName));
    private static readonly Lazy<IReadOnlyDictionary<string, Point>> LazyIngameRegionPositions = new(() => LoadNamedPoints(IngameRegionPositionsResourceName));
    private static readonly Lazy<double?> LazyIngameRegionScale = new(() => LoadScale(IngameRegionPositionsResourceName));

    /// <summary>Solar system id -> Dotlan's own local (x, y) pixel position within its region's SVG canvas.</summary>
    public static IReadOnlyDictionary<int, Point> Positions => LazyPositions.Value;

    /// <summary>Normalized region name -> curated (x, y) anchor on the in-game universe grid.</summary>
    public static IReadOnlyDictionary<string, Point> IngameRegionPositions => LazyIngameRegionPositions.Value;

    /// <summary>
    /// Optional uniform factor (the JSON's <c>_scale</c> key) the curated 0-100 grid should be scaled
    /// by to reach world space. When present it is authoritative, so the hand-tuned arrangement renders
    /// exactly as authored; when absent <see cref="SchematicMapLayout"/> derives a scale from footprints.
    /// </summary>
    public static double? IngameRegionScale => LazyIngameRegionScale.Value;

    /// <summary>Normalizes a region name for lookup: trimmed, lower-cased, internal whitespace collapsed.</summary>
    public static string NormalizeRegionName(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

    private static double? LoadScale(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.TryGetProperty("_scale", out var scale) &&
            scale.ValueKind == JsonValueKind.Number &&
            scale.GetDouble() > 0)
        {
            return scale.GetDouble();
        }
        return null;
    }

    private static IReadOnlyDictionary<string, Point> LoadNamedPoints(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return new Dictionary<string, Point>();

        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, Point>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            // Skip metadata keys (e.g. "_comment") and anything that isn't an [x, y] pair.
            if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() < 2) continue;
            double x = property.Value[0].GetDouble();
            double y = property.Value[1].GetDouble();
            result[NormalizeRegionName(property.Name)] = new Point(x, y);
        }
        return result;
    }
}
