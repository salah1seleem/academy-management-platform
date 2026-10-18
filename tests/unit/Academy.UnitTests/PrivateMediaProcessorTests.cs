using System.Text;
using Academy.Api.Operations;
using Microsoft.Extensions.Configuration;

namespace Academy.UnitTests;

public sealed class PrivateMediaProcessorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("academity-media-unit-").FullName;
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    private static PrivateMediaProcessor Processor(Dictionary<string,string?>? settings = null) => new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
    private static Task<string> FF(params string[] args) => PrivateMediaProcessor.Run("ffmpeg", ["-nostdin", "-v", "error", "-threads", "1", "-filter_threads", "1", ..args], TimeSpan.FromSeconds(10), CancellationToken.None);
    private async Task<string> PixelHash(string path) => (await FF("-i", path, "-frames:v", "1", "-map_metadata", "-1", "-pix_fmt", "rgba", "-f", "framemd5", "-")).Split('\n').Single(s=>s.StartsWith("0,"));

    [Fact] public async Task Decode_reencode_removes_png_location_text_but_preserves_pixels()
    {
        var data = Encoding.UTF8.GetBytes("location\0GPS=30.000,31.000; Synthetic-camera-owner; 2026-10-18");
        var chunk = Chunk("tEXt", data);
        var original = Pixel[..^12].Concat(chunk).Concat(Pixel[^12..]).ToArray();
        var input=Path.Combine(root,"original.png"); await File.WriteAllBytesAsync(input,original);
        using var processor=Processor();using var result=await processor.Prepare(new MemoryStream(original),"image/png",root,CancellationToken.None);
        Assert.Equal("image/png",result.ContentType);Assert.Equal(new FileInfo(result.Path).Length,result.Length);
        var bytes=await File.ReadAllBytesAsync(result.Path);var text=Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("location",text);Assert.DoesNotContain("GPS",text);Assert.DoesNotContain("Synthetic-camera-owner",text);Assert.DoesNotContain("script",text);
        Assert.Equal(await PixelHash(input),await PixelHash(result.Path));
        var processedDirectory=Path.GetDirectoryName(result.Path)!;result.Dispose();Assert.False(Directory.Exists(processedDirectory));Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root,".processing")));
    }
    [Theory][InlineData("mjpeg","image/jpeg","jpeg")][InlineData("webp","image/webp","webp")]
    public async Task Jpeg_and_webp_are_decoded_to_one_private_png_not_served_as_original_bytes(string codec,string mime,string extension)
    {
        var input=Path.Combine(root,"sample."+extension);
        if(codec=="webp") await File.WriteAllBytesAsync(input,Convert.FromBase64String("UklGRkwAAABXRUJQVlA4IEAAAABQAwCdASogABgAPpFCnEolo6KhqAgAsBIJZQDGqoAAQFEcAAD+7tPf/uOwN14t//ucD/ucD/ucD+NspCdnCgAA")); // Synthetic red 32x24, generated once with cwebp. Runtime only needs a decoder.
        else await FF("-f","lavfi","-i","color=c=red:s=32x24:d=0.1","-frames:v","1","-c:v",codec,"-update","1",input);
        using var processor=Processor();await using var file=File.OpenRead(input);using var result=await processor.Prepare(file,mime,root,CancellationToken.None);
        Assert.Equal("image/png",result.ContentType);Assert.Equal(await PixelHash(input),await PixelHash(result.Path));
    }
    [Theory][InlineData("mp4","video/mp4")][InlineData("mov","video/quicktime")]
    public async Task Video_reencoding_removes_location_owner_time_metadata_and_preserves_decodable_video_and_audio(string extension,string mime)
    {
        var input=Path.Combine(root,"sample."+extension);
        await FF("-f","lavfi","-i","color=c=red:s=320x180:r=25:d=1","-f","lavfi","-i","sine=frequency=1000:duration=1","-c:v","mpeg4","-c:a","aac","-metadata","location=+30.0000+031.0000/","-metadata","title=Synthetic-camera-owner","-metadata","creation_time=2020-01-01T00:00:00Z",input);
        var original=await PrivateMediaProcessor.Run("ffprobe",["-v","error","-show_format","-show_streams","-of","json",input],TimeSpan.FromSeconds(5),CancellationToken.None);Assert.Contains("location",original);Assert.Contains("Synthetic-camera-owner",original);
        using var processor=Processor();await using var file=File.OpenRead(input);using var result=await processor.Prepare(file,mime,root,CancellationToken.None);
        Assert.Equal("video/mp4",result.ContentType);
        var report=await PrivateMediaProcessor.Run("ffprobe",["-v","error","-show_format","-show_streams","-of","json",result.Path],TimeSpan.FromSeconds(5),CancellationToken.None);
        Assert.DoesNotContain("\"location",report);Assert.DoesNotContain("+30.000",report);Assert.DoesNotContain("Synthetic-camera-owner",report);Assert.DoesNotContain("2020-01-01",report);Assert.Contains("h264",report);Assert.Contains("aac",report);
        await FF("-xerror","-i",result.Path,"-f","null","-"); // Decode all resulting video/audio, not just the ftyp header.
    }
    [Fact] public async Task Valid_magic_with_corrupt_body_and_oversized_dimensions_are_rejected_without_output()
    {
        using var processor=Processor();
        foreach(var bytes in new[]{Pixel[..20],Pixel[..33].Concat(new byte[12]).ToArray(),Pixel.Concat(Encoding.UTF8.GetBytes("<script>synthetic trailing payload</script>")).ToArray()})
        {
            var error=await Assert.ThrowsAsync<MediaProcessingException>(()=>processor.Prepare(new MemoryStream(bytes),"image/png",root,CancellationToken.None));Assert.Equal(400,error.Status);
        }
        var huge=Path.Combine(root,"huge.png");await FF("-f","lavfi","-i","color=c=white:s=9000x2:d=0.1","-frames:v","1","-c:v","png","-update","1",huge);
        await using var input=File.OpenRead(huge);Assert.Equal(400,(await Assert.ThrowsAsync<MediaProcessingException>(()=>processor.Prepare(input,"image/png",root,CancellationToken.None))).Status);
        var staging=Path.Combine(root,".processing");Assert.True(!Directory.Exists(staging)||!Directory.EnumerateFileSystemEntries(staging).Any());
    }
    [Fact] public async Task Missing_inspection_dependency_fails_closed_and_does_not_retain_the_raw_file()
    {
        using var processor=Processor(new(){["Media:FFprobePath"]=Path.Combine(root,"missing-probe")});
        var error=await Assert.ThrowsAsync<MediaProcessingException>(()=>processor.Prepare(new MemoryStream(Pixel),"image/png",root,CancellationToken.None));Assert.Equal(503,error.Status);Assert.DoesNotContain("missing-probe",error.Message);
        var staging=Path.Combine(root,".processing");Assert.True(!Directory.Exists(staging)||!Directory.EnumerateFileSystemEntries(staging).Any());
    }
    [Fact] public async Task Explicit_cancellation_and_process_deadline_stop_the_child_instead_of_accepting_uninspected_data()
    {
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();using var processor=Processor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>processor.Prepare(new MemoryStream(Pixel),"image/png",root,cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PrivateMediaProcessor.Run("ffmpeg",["-nostdin","-v","error","-re","-f","lavfi","-i","color=c=black:s=16x16:r=1:d=10","-f","null","-"],TimeSpan.FromMilliseconds(300),CancellationToken.None));
    }
    [Fact] public async Task Only_two_inspections_run_at_once_and_overflow_is_not_queued_or_published()
    {
        using var processor=Processor();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var a=new GatedStream(Pixel,gate.Task);using var b=new GatedStream(Pixel,gate.Task);
        var first=processor.Prepare(a,"image/png",root,CancellationToken.None);var second=processor.Prepare(b,"image/png",root,CancellationToken.None);
        try
        {
            var busy=await Assert.ThrowsAsync<MediaProcessingException>(()=>processor.Prepare(new MemoryStream(Pixel),"image/png",root,CancellationToken.None));Assert.Equal(503,busy.Status);Assert.Contains("مشغول",busy.Message);
        }
        finally {gate.TrySetResult();foreach(var result in await Task.WhenAll(first,second))result.Dispose();}
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root,".processing")));
    }
    private sealed class GatedStream(byte[] data,Task gate):MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await gate.WaitAsync(token);return await base.ReadAsync(buffer,token);}
    }
    private static byte[] Chunk(string type,byte[] data)
    {
        var kind=Encoding.ASCII.GetBytes(type);var body=kind.Concat(data).ToArray();uint crc=0xffffffff;
        foreach(var b in body){crc^=b;for(var i=0;i<8;i++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}
        return BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length)).Concat(body).Concat(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(unchecked((int)~crc)))).ToArray();
    }
    public void Dispose()=>Directory.Delete(root,true);
}
