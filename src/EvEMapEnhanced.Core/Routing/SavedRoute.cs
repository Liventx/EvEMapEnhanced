namespace EvEMapEnhanced.Core.Routing;

/// <summary>A named, persisted route the user chose to keep for later reuse.</summary>
public sealed class SavedRoute
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<RouteStep> Steps { get; set; } = new();
}
