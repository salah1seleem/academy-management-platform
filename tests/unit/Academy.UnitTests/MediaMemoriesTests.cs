using Academy.Api.Operations;
namespace Academy.UnitTests;
public sealed class MediaMemoriesTests
{
 [Fact]public void A_metadata_index_never_silently_truncates_at_its_limit(){Assert.True(AssetEndpoints.MemoryIndexFits(0));Assert.True(AssetEndpoints.MemoryIndexFits(1000));Assert.False(AssetEndpoints.MemoryIndexFits(1001));Assert.False(AssetEndpoints.MemoryIndexFits(-1));}
}
