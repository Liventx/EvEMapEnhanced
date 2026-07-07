using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.Sde;

namespace EvEMapEnhanced.Data.Stats;

/// <summary>
/// Computes <see cref="SystemStats"/> for a solar system from live zKillboard data.
/// Total kill count / ISK destroyed come directly from zKillboard's per-system feed.
/// Capital-kill and pod-kill counts require the victim's ship type, which is only
/// available in the full ESI killmail -- to bound API usage, only the most recent
/// <see cref="HydrateTopN"/> kills are hydrated per call.
/// </summary>
public sealed class SystemStatsService
{
    private readonly ZkillClient _zkill;
    private readonly EsiKillmailClient _esi;
    private readonly ShipTypeCatalog? _catalog;

    public int HydrateTopN { get; init; } = 20;

    public SystemStatsService(ZkillClient zkill, EsiKillmailClient esi, ShipTypeCatalog? catalog)
    {
        _zkill = zkill;
        _esi = esi;
        _catalog = catalog;
    }

    public async Task<SystemStats> ComputeAsync(int solarSystemId, CancellationToken ct = default)
    {
        var lastHour = await _zkill.GetRecentKillmailsAsync(solarSystemId, 3600, ct);
        var last24h = await _zkill.GetRecentKillmailsAsync(solarSystemId, 86400, ct);

        int capitalKills = 0, podKills = 0;

        if (_catalog is not null)
        {
            foreach (var kill in last24h.Take(HydrateTopN))
            {
                int? shipTypeId = await _esi.GetVictimShipTypeIdAsync(kill.KillmailId, kill.Hash, ct);
                if (shipTypeId is null) continue;

                if (_catalog.IsCapitalTypeId(shipTypeId.Value)) capitalKills++;
                else if (_catalog.IsPodTypeId(shipTypeId.Value)) podKills++;
            }
        }

        double iskDestroyed = last24h.Sum(k => k.TotalValue);

        return new SystemStats(
            solarSystemId,
            KillsLastHour: lastHour.Count,
            KillsLast24H: last24h.Count,
            CapitalKillsLast24H: capitalKills,
            PodKillsLast24H: podKills,
            IskDestroyedLast24H: iskDestroyed,
            LastUpdatedUtc: DateTime.UtcNow);
    }
}
