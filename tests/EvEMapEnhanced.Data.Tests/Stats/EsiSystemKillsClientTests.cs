using System.Net;
using System.Text;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class EsiSystemKillsClientTests
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
    public async Task GetNpcKillsPerSystemAsync_ParsesEsiSnakeCaseFields()
    {
        // Shape of ESI's real /universe/system_kills/ response: snake_case field names.
        const string json = """
            [
                { "system_id": 30000142, "npc_kills": 12, "ship_kills": 3, "pod_kills": 1 },
                { "system_id": 30002410, "npc_kills": 0, "ship_kills": 42, "pod_kills": 24 }
            ]
            """;

        using var httpClient = new HttpClient(new StubHandler(json));
        var client = new EsiSystemKillsClient(httpClient);

        var result = await client.GetNpcKillsPerSystemAsync();

        Assert.Equal(12, result[30000142]);
        Assert.Equal(0, result[30002410]);
    }

    [Fact]
    public async Task GetNpcKillsPerSystemAsync_EmptyResponseReturnsEmptyDictionary()
    {
        using var httpClient = new HttpClient(new StubHandler("[]"));
        var client = new EsiSystemKillsClient(httpClient);

        var result = await client.GetNpcKillsPerSystemAsync();

        Assert.Empty(result);
    }
}
