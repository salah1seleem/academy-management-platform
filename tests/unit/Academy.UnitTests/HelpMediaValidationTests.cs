using Academy.Api.Help;

namespace Academy.UnitTests;
public class HelpMediaValidationTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Help", name));
    [Fact] public void Actual_synthetic_h264_has_server_measured_duration() => Assert.Equal(2, HelpMediaValidation.Mp4Duration(Fixture("synthetic-guide.mp4")));
    [Fact] public void Png_container_checks_crc_dimensions_data_and_end() => Assert.True(HelpMediaValidation.Png(Fixture("synthetic-thumbnail.png")));
    [Fact] public void Truncated_or_renamed_containers_are_not_valid_media()
    {
        var video = Fixture("synthetic-guide.mp4"); var png = Fixture("synthetic-thumbnail.png");
        foreach (var length in new[] { 0, 8, 32, video.Length / 2, video.Length - 1 }) Assert.Null(HelpMediaValidation.Mp4Duration(video[..length]));
        Assert.Null(HelpMediaValidation.Mp4Duration(png)); Assert.False(HelpMediaValidation.Png(video));
        Assert.Null(HelpMediaValidation.Mp4Duration("\0\0\0\x18ftypisom"u8.ToArray()));
        Assert.False(HelpMediaValidation.Png(png[..^1])); var corrupt = (byte[])png.Clone(); corrupt[20] ^= 1; Assert.False(HelpMediaValidation.Png(corrupt));
        Assert.False(HelpMediaValidation.Png([.. png, 0]));
    }
    [Fact] public void Unsupported_video_codec_and_zero_clock_are_rejected()
    {
        var b = Fixture("synthetic-guide.mp4"); var avc = b.AsSpan().LastIndexOf("avc1"u8); Assert.True(avc > 0); "hev1"u8.CopyTo(b.AsSpan(avc)); Assert.Null(HelpMediaValidation.Mp4Duration(b));
        b = Fixture("synthetic-guide.mp4"); var mvhd = b.AsSpan().IndexOf("mvhd"u8); Assert.True(mvhd > 0); b.AsSpan(mvhd + 16, 4).Clear(); Assert.Null(HelpMediaValidation.Mp4Duration(b));
    }
}
