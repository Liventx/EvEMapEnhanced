namespace EvEMapEnhanced.Core.Models;

public sealed record Region(int Id, string Name);

public sealed record Constellation(int Id, string Name, int RegionId);

/// <summary>An undirected stargate connection between two solar systems.</summary>
public sealed record Stargate(int FromSystemId, int ToSystemId);
