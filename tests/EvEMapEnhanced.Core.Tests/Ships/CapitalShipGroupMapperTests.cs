using EvEMapEnhanced.Core.Ships;

namespace EvEMapEnhanced.Core.Tests.Ships;

public class CapitalShipGroupMapperTests
{
    [Theory]
    [InlineData(659, CapitalShipClass.Supercarrier)]
    [InlineData(547, CapitalShipClass.Carrier)]
    [InlineData(898, CapitalShipClass.BlackOps)]
    [InlineData(30, CapitalShipClass.Titan)]
    public void TryMapGroupId_MapsJumpCapitalGroups(int groupId, CapitalShipClass expected)
    {
        Assert.True(CapitalShipGroupMapper.TryMapGroupId(groupId, out var shipClass));
        Assert.Equal(expected, shipClass);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(29)]
    public void TryMapGroupId_RejectsNonJumpGroups(int groupId)
    {
        Assert.False(CapitalShipGroupMapper.TryMapGroupId(groupId, out _));
    }
}
