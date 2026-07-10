using System.Net;
using System.Text;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class EsiIncursionsClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task GetSanshaInfestedSystemIdsAsync_ParsesInfestedSystems()
    {
        const string json = """
            [
                {
                    "constellation_id": 20000377,
                    "faction_id": 500019,
                    "infested_solar_systems": [30032547, 30002568, 30002569],
                    "staging_solar_system_id": 30042547,
                    "state": "established",
                    "type": "Incursion"
                },
                {
                    "constellation_id": 20000160,
                    "faction_id": 500019,
                    "infested_solar_systems": [30001091, 30001092],
                    "staging_solar_system_id": 30001096,
                    "state": "withdrawing",
                    "type": "Incursion"
                }
            ]
            """;

        using var httpClient = new HttpClient(new StubHandler(json));
        var client = new EsiIncursionsClient(httpClient);

        var result = await client.GetSanshaInfestedSystemIdsAsync();

        Assert.Equal(5, result.Count);
        Assert.Contains(30032547, result);
        Assert.Contains(30001092, result);
    }

    [Fact]
    public async Task GetSanshaInfestedSystemIdsAsync_IgnoresNonSanshaFactions()
    {
        const string json = """
            [
                {
                    "faction_id": 500019,
                    "infested_solar_systems": [30000142]
                },
                {
                    "faction_id": 500020,
                    "infested_solar_systems": [30000143]
                }
            ]
            """;

        using var httpClient = new HttpClient(new StubHandler(json));
        var client = new EsiIncursionsClient(httpClient);

        var result = await client.GetSanshaInfestedSystemIdsAsync();

        Assert.Single(result);
        Assert.Contains(30000142, result);
    }

    [Fact]
    public async Task GetSanshaInfestedSystemIdsAsync_EmptyResponseReturnsEmptySet()
    {
        using var httpClient = new HttpClient(new StubHandler("[]"));
        var client = new EsiIncursionsClient(httpClient);

        var result = await client.GetSanshaInfestedSystemIdsAsync();

        Assert.Empty(result);
    }
}
