using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvEMapEnhanced.Data.Tests.Sde;

public class EsiUniverseTypeClientTests
{
    [Fact]
    public void EsiTypeDto_DeserializesGroupId()
    {
        const string json = """{"group_id": 659, "name": "Hel", "published": true}""";

        var dto = JsonSerializer.Deserialize<EsiTypeDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(659, dto!.GroupId);
    }

    private sealed class EsiTypeDto
    {
        [JsonPropertyName("group_id")]
        public int GroupId { get; set; }
    }
}
