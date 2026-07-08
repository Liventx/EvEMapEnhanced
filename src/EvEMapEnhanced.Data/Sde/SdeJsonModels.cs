using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>
/// DTOs matching the official CCP Static Data Export JSON Lines schema
/// (developers.eveonline.com/static-data), used only during import.
/// Deserialization uses case-insensitive property matching, so most fields
/// bind without explicit [JsonPropertyName] (e.g. "regionID" -> RegionId).
/// </summary>
internal sealed class SdePosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

internal sealed class SdeRegionDto
{
    [JsonPropertyName("_key")]
    public int Key { get; set; }
    public Dictionary<string, string>? Name { get; set; }
    public SdePosition? Position { get; set; }
}

internal sealed class SdeConstellationDto
{
    [JsonPropertyName("_key")]
    public int Key { get; set; }
    public Dictionary<string, string>? Name { get; set; }
    public int RegionId { get; set; }
    public SdePosition? Position { get; set; }
}

internal sealed class SdeSolarSystemDto
{
    [JsonPropertyName("_key")]
    public int Key { get; set; }
    public Dictionary<string, string>? Name { get; set; }
    public int ConstellationId { get; set; }
    public int RegionId { get; set; }
    public double SecurityStatus { get; set; }
    public SdePosition? Position { get; set; }
}

internal sealed class SdeStargateDestinationDto
{
    public int SolarSystemId { get; set; }
    public int StargateId { get; set; }
}

internal sealed class SdeStargateDto
{
    [JsonPropertyName("_key")]
    public int Key { get; set; }
    public int SolarSystemId { get; set; }
    public SdeStargateDestinationDto? Destination { get; set; }
}

internal sealed class SdeTypeDto
{
    [JsonPropertyName("_key")]
    public int Key { get; set; }
    public Dictionary<string, string>? Name { get; set; }
    public int GroupId { get; set; }
    public double Mass { get; set; }
    public bool Published { get; set; } = true;
}
