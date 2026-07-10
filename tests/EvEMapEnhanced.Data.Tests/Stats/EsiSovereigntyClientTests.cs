using System.Net;
using System.Text;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Data.Tests.Stats;

public class EsiSovereigntyClientTests
{
    private sealed class RouteStubHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod Method, string Path), string> _responses;

        public RouteStubHandler(Dictionary<(HttpMethod Method, string Path), string> responses) =>
            _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            if (!_responses.TryGetValue((request.Method, path), out string? json))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task GetIhubAllianceNamesBySystemAsync_ResolvesAllianceNamesForOccupiedSystems()
    {
        const string mapJson = """
            [
                { "system_id": 30000208, "alliance_id": 99003581, "corporation_id": 98599770 },
                { "system_id": 30000209, "alliance_id": 99003581, "corporation_id": 98599770 },
                { "system_id": 30000001, "faction_id": 500007 }
            ]
            """;
        const string namesJson = """
            [
                { "category": "alliance", "id": 99003581, "name": "Test Alliance" }
            ]
            """;

        var routes = new Dictionary<(HttpMethod, string), string>
        {
            [(HttpMethod.Get, "/latest/sovereignty/map")] = mapJson,
            [(HttpMethod.Post, "/latest/universe/names")] = namesJson,
        };

        using var httpClient = new HttpClient(new RouteStubHandler(routes));
        var client = new EsiSovereigntyClient(httpClient);

        var result = await client.GetIhubAllianceNamesBySystemAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Test Alliance", result[30000208]);
        Assert.Equal("Test Alliance", result[30000209]);
        Assert.False(result.ContainsKey(30000001));
    }

    [Fact]
    public async Task GetIhubAllianceNamesBySystemAsync_EmptyMapReturnsEmptyDictionary()
    {
        var routes = new Dictionary<(HttpMethod, string), string>
        {
            [(HttpMethod.Get, "/latest/sovereignty/map")] = "[]",
        };

        using var httpClient = new HttpClient(new RouteStubHandler(routes));
        var client = new EsiSovereigntyClient(httpClient);

        var result = await client.GetIhubAllianceNamesBySystemAsync();

        Assert.Empty(result);
    }
}
