using System.Text.Json;
using System.Text.Json.Serialization;
using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Data.Tests.Auth;

public class EsiCharacterShipClientTests
{
    [Fact]
    public void EsiShipDto_DeserializesShipTypeId()
    {
        const string json = """{"ship_item_id": 1, "ship_name": "Archon", "ship_type_id": 23757}""";

        var dto = JsonSerializer.Deserialize<EsiCharacterShipClientTestsHelper.EsiShipDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(23757, dto!.ShipTypeId);
    }
}

/// <summary>Exposes the private DTO for deserialization tests without HTTP.</summary>
internal static class EsiCharacterShipClientTestsHelper
{
    internal sealed class EsiShipDto
    {
        [JsonPropertyName("ship_type_id")]
        public int ShipTypeId { get; set; }
    }
}
