using Academy.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Academy.UnitTests;
public sealed class AppPlatformStorageTests
{
    private sealed class EnvironmentStub(string root, string name = "Testing") : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Fact]
    public async Task Local_compatibility_survives_new_instance_and_supports_seek_and_delete()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Media:PrivateRoot"] = root }).Build();
        var key = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        try {
            using(var first = new MediaObjectStore(config,new EnvironmentStub(root)))
                await first.Put(key,new MemoryStream([1,2,3]));
            using var second = new MediaObjectStore(config,new EnvironmentStub(root));
            await using(var bytes = await second.Open(key)) {
                Assert.NotNull(bytes); Assert.True(bytes.CanSeek); bytes.Seek(1,SeekOrigin.Begin); Assert.Equal(2,bytes.ReadByte());
            }
            await Assert.ThrowsAsync<ArgumentException>(()=>second.Open("../private"));
            await second.Delete(key); Assert.Null(await second.Open(key));
        } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public void Production_rejects_unencrypted_spaces_transport()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Media:Provider"]="Spaces",["Spaces:Bucket"]="test",["Spaces:Endpoint"]="http://127.0.0.1:9000"
        }).Build();
        Assert.Throws<InvalidOperationException>(()=>new MediaObjectStore(config,new EnvironmentStub("/tmp","Production")));
    }
}
