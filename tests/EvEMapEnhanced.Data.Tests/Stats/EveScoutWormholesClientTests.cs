using System.Net;
using System.Text;
using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class EveScoutWormholesClientTests
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
    public async Task GetActiveConnectionsAsync_ParsesTheraAndTurnurConnections()
    {
        const string json = """
            [
                {
                    "id": "69355",
                    "completed": true,
                    "wh_type": "Q063",
                    "max_ship_size": "medium",
                    "remaining_hours": 1,
                    "expires_at": "2026-07-11T12:47:43.000Z",
                    "wh_exits_outward": true,
                    "out_system_id": 31000005,
                    "out_system_name": "Thera",
                    "out_signature": "NQX-048",
                    "in_system_id": 30001648,
                    "in_system_name": "Adahum",
                    "in_signature": "EIM-956"
                },
                {
                    "id": "69329",
                    "completed": true,
                    "wh_type": "J377",
                    "max_ship_size": "medium",
                    "remaining_hours": 2,
                    "expires_at": "2026-07-11T13:40:24.000Z",
                    "wh_exits_outward": false,
                    "out_system_id": 30002086,
                    "out_system_name": "Turnur",
                    "out_signature": "NBO-638",
                    "in_system_id": 31001022,
                    "in_system_name": "J120452",
                    "in_signature": "URI-586"
                },
                {
                    "id": "99999",
                    "completed": true,
                    "wh_type": "C140",
                    "max_ship_size": "small",
                    "wh_exits_outward": true,
                    "out_system_id": 30000142,
                    "out_system_name": "Jita",
                    "out_signature": "AAA-111",
                    "in_system_id": 30002187,
                    "in_system_name": "Amarr",
                    "in_signature": "BBB-222"
                },
                {
                    "id": "88888",
                    "completed": false,
                    "wh_type": "Q063",
                    "wh_exits_outward": true,
                    "out_system_id": 31000005,
                    "out_system_name": "Thera",
                    "out_signature": "XXX-000",
                    "in_system_id": 30000142,
                    "in_system_name": "Jita",
                    "in_signature": "YYY-000"
                }
            ]
            """;

        using var httpClient = new HttpClient(new StubHandler(json));
        var client = new EveScoutWormholesClient(httpClient);

        var result = await client.GetActiveConnectionsAsync();

        Assert.Equal(2, result.Count);

        var thera = result.Single(c => c.Hub == WormholeHubKind.Thera);
        Assert.Equal("69355", thera.Id);
        Assert.Equal(30001648, thera.RemoteSystemId);
        Assert.Equal("Adahum", thera.RemoteSystemName);
        Assert.Equal("Q063", thera.WhType);
        Assert.True(thera.ExitsOutward);

        var turnur = result.Single(c => c.Hub == WormholeHubKind.Turnur);
        Assert.Equal(30002086, turnur.HubSystemId);
        Assert.Equal("J120452", turnur.RemoteSystemName);
        Assert.False(turnur.ExitsOutward);
    }

    [Fact]
    public async Task GetActiveConnectionsAsync_EmptyResponseReturnsEmptyList()
    {
        using var httpClient = new HttpClient(new StubHandler("[]"));
        var client = new EveScoutWormholesClient(httpClient);

        var result = await client.GetActiveConnectionsAsync();

        Assert.Empty(result);
    }
}
