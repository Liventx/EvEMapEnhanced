using System.Net;
using System.Text;
using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class ZKillboardSystemKillsClientTests
{
    private sealed class RegionStubHandler : HttpMessageHandler
    {
        private readonly string _json;
        public int RequestCount { get; private set; }

        public RegionStubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            Assert.Contains("regionID/10000012", request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task GetActivityLevelsAsync_FetchesByRegionAndClassifiesTargetSystems()
    {
        var now = DateTime.UtcNow;
        string json = $$"""
            [
              {
                "killmail_id": 1,
                "killmail_time": "{{now.AddMinutes(-10):O}}",
                "solar_system_id": 30000142,
                "victim": { "ship_type_id": 587 },
                "zkb": { "npc": false }
              },
              {
                "killmail_id": 2,
                "killmail_time": "{{now.AddMinutes(-15):O}}",
                "solar_system_id": 30000143,
                "victim": { "ship_type_id": 670 },
                "zkb": { "npc": false }
              }
            ]
            """;

        using var httpClient = new HttpClient(new RegionStubHandler(json));
        var client = new ZKillboardSystemKillsClient(httpClient);
        var filter = new KillVictimFilter(new HashSet<int> { 670 });

        var result = await client.GetActivityLevelsAsync(
            new[] { 30000142, 30000143, 30009999 },
            id => id switch
            {
                30000142 => 10000012,
                30000143 => 10000012,
                30009999 => 10000012,
                _ => null,
            },
            filter);

        Assert.Equal(PvPActivityLevel.Recent, result[30000142].Level);
        Assert.Equal(1, result[30000142].ValidHourKillCount);
        Assert.Equal(PvPActivityLevel.None, result[30000143].Level);
        Assert.Equal(PvPActivityLevel.None, result[30009999].Level);
    }

    [Fact]
    public async Task GetActivityLevelsAsync_OnRegionFailure_KeepsPreviousActivity()
    {
        using var httpClient = new HttpClient(new ThrowingHandler());
        var client = new ZKillboardSystemKillsClient(httpClient);
        var filter = new KillVictimFilter(new HashSet<int>());
        var previous = new Dictionary<int, PvPActivityStats>
        {
            [30000142] = new(PvPActivityLevel.Hot, 5),
        };

        var result = await client.GetActivityLevelsAsync(
            new[] { 30000142 },
            _ => 10000012,
            filter,
            previousActivity: previous);

        Assert.Equal(PvPActivityLevel.Hot, result[30000142].Level);
        Assert.Equal(5, result[30000142].ValidHourKillCount);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("network down");
    }
}
