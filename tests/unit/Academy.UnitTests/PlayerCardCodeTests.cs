using Academy.Infrastructure.Attendance;

namespace Academy.UnitTests;
public class PlayerCardCodeTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("player-123")] [InlineData("https://example.test/player?name=child")]
    public void Wrong_payload_is_rejected(string? value) => Assert.False(PlayerCardCode.IsWellFormed(value));
    [Fact] public void Code_is_random_256_bit_opaque_and_hash_is_not_the_payload()
    {
        var first = PlayerCardCode.Create(); var second = PlayerCardCode.Create(); Assert.NotEqual(first, second); Assert.Equal(84, first.Length); Assert.True(PlayerCardCode.IsWellFormed(first)); Assert.Equal(64, PlayerCardCode.Hash(first).Length); Assert.NotEqual(first, PlayerCardCode.Hash(first)); Assert.Equal(PlayerCardCode.Hash(first), PlayerCardCode.Hash(first));
    }
    [Theory] [InlineData(63, 'A')] [InlineData(65, 'A')] [InlineData(64, 'G')]
    public void Truncated_extended_or_non_hex_code_is_rejected(int length, char value) => Assert.False(PlayerCardCode.IsWellFormed(PlayerCardCode.Prefix + new string(value, length)));
}
