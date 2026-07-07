namespace EvEMapEnhanced.Core.Notes;

/// <summary>A persistent user note attached to a solar system, with free-form tags.</summary>
public sealed class SystemNote
{
    public int SolarSystemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}
