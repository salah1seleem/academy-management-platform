using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Academy.Api.Operations;

// Decode untrusted bytes out of process. Only newly encoded pixels/samples may be published;
// uploaded metadata, extra tracks, original names and original bytes are never the output.
public sealed class PrivateMediaProcessor(IConfiguration configuration) : IDisposable
{
    public const int ImageLimit = 8 * 1024 * 1024;
    public const int VideoLimit = 50 * 1024 * 1024;
    public const int MaxVideoSeconds = 120;
    private readonly SemaphoreSlim slots = new(2, 2);
    private bool toolsVerified;

    public async Task<ProcessedMedia> Prepare(Stream source, string contentType, string privateRoot, CancellationToken cancellationToken)
    {
        if (!await slots.WaitAsync(0, cancellationToken)) throw new MediaProcessingException(503, "فحص الملفات مشغول؛ حاول بعد قليل. لم يُنشر الملف.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(50));
        var processingToken = deadline.Token;
        string? work = null;
        try
        {
            if (!toolsVerified)
            {
                await Run(configuration["Media:FFprobePath"] ?? "ffprobe", ["-version"], TimeSpan.FromSeconds(5), processingToken);
                foreach (var encoder in new[] { "png", "libx264", "aac" })
                {
                    var help = await Run(configuration["Media:FFmpegPath"] ?? "ffmpeg", ["-hide_banner", "-h", "encoder=" + encoder], TimeSpan.FromSeconds(5), processingToken);
                    if (!help.Contains("Encoder " + encoder + " [", StringComparison.Ordinal))
                        throw new MediaProcessingException(503, "فحص الملفات غير متاح الآن؛ لم يُنشر الملف. تواصل مع الإدارة.");
                }
                toolsVerified = true; // A failed capability check is never cached as success.
            }
            var video = contentType.StartsWith("video/", StringComparison.Ordinal);
            var format = contentType switch { "image/jpeg" => "jpeg_pipe", "image/png" => "png_pipe", "image/webp" => "webp_pipe", "video/mp4" or "video/quicktime" => "mov", _ => throw Invalid() };
            Directory.CreateDirectory(privateRoot);
            var staging = Path.Combine(privateRoot, ".processing");
            Directory.CreateDirectory(staging);
            work = Path.Combine(staging, Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(work);
            else Directory.CreateDirectory(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var input = Path.Combine(work, "input");
            var output = Path.Combine(work, video ? "output.mp4" : "output.png");
            await using (var file = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; var total = 0; int read;
                while ((read = await source.ReadAsync(buffer, processingToken)) > 0)
                {
                    total += read;
                    if (total > (video ? VideoLimit : ImageLimit)) throw Invalid();
                    await file.WriteAsync(buffer.AsMemory(0, read), processingToken);
                }
            }
            var before = await Probe(input, format, processingToken);
            Validate(before, video);
            var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-max_alloc", "67108864", "-xerror", "-filter_threads", "1", "-threads", "1", "-err_detect", "explode", "-protocol_whitelist", "file", "-f", format, "-i", input,
                "-map", "0:v:0", "-map_metadata", "-1", "-map_metadata:s", "-1", "-map_chapters", "-1", "-sn", "-dn", "-threads", "1" };
            if (video) args.InsertRange(args.IndexOf("-i"), ["-enable_drefs", "0", "-use_absolute_path", "0"]);
            if (video)
                args.AddRange(["-map", "0:a:0?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-r", "30", "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-metadata:s:v:0", "rotate=0", "-movflags", "+faststart", "-fs", (VideoLimit + 1).ToString(CultureInfo.InvariantCulture)]);
            else
                args.AddRange(["-an", "-frames:v", "1", "-c:v", "png", "-pix_fmt", "rgba", "-f", "image2", "-update", "1", "-fs", (ImageLimit + 1).ToString(CultureInfo.InvariantCulture)]);
            args.Add(output);
            await Run(configuration["Media:FFmpegPath"] ?? "ffmpeg", args, TimeSpan.FromSeconds(40), processingToken);
            var length = new FileInfo(output).Length;
            if (length < 12 || length > (video ? VideoLimit : ImageLimit)) throw Invalid();
            var after = await Probe(output, video ? "mov" : "png_pipe", processingToken);
            Validate(after, video);
            if (video && Math.Abs(before.Duration - after.Duration) > 0.25) throw Invalid(); // No silently truncated clips (including the output size cap).
            var result = new ProcessedMedia(work, output, video ? "video/mp4" : "image/png", length);
            work = null; // Caller owns this one generated directory, even if the DB write later fails.
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new MediaProcessingException(503, "تعذر إكمال فحص الملف في الوقت المتاح. جرّب ملفًا أصغر؛ لم يُنشر الملف."); }
        catch (Win32Exception) { throw new MediaProcessingException(503, "فحص الملفات غير متاح الآن؛ لم يُنشر الملف. تواصل مع الإدارة."); }
        catch (JsonException) { throw Invalid(); }
        catch (KeyNotFoundException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        finally
        {
            if (work != null) Directory.Delete(work, true);
            slots.Release();
        }
    }

    private async Task<MediaShape> Probe(string input, string format, CancellationToken token)
    {
        var args = new List<string> { "-v", "error", "-max_alloc", "67108864", "-probesize", "8388608", "-analyzeduration", "5000000", "-protocol_whitelist", "file", "-f", format, "-show_entries", "stream=codec_type,codec_name,width,height,avg_frame_rate,nb_frames:format=duration", "-of", "json", input };
        if (format == "mov") args.InsertRange(args.Count - 1, ["-enable_drefs", "0", "-use_absolute_path", "0"]);
        var json = await Run(configuration["Media:FFprobePath"] ?? "ffprobe", args, TimeSpan.FromSeconds(5), token);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var images = streams.Where(s => s.GetProperty("codec_type").GetString() == "video").ToArray();
        if (streams.Length > 16 || images.Length != 1) throw Invalid();
        var v = images[0];
        var frames = v.TryGetProperty("nb_frames", out var n) && long.TryParse(n.GetString(), out var count) ? count : 1;
        var duration = doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        var rate = v.GetProperty("avg_frame_rate").GetString()!.Split('/');
        var fps = rate.Length == 2 && double.TryParse(rate[0], CultureInfo.InvariantCulture, out var numerator) && double.TryParse(rate[1], CultureInfo.InvariantCulture, out var denominator) && denominator > 0 ? numerator / denominator : 0;
        return new(v.GetProperty("width").GetInt32(), v.GetProperty("height").GetInt32(), duration, fps, frames, v.GetProperty("codec_name").GetString()!);
    }

    private static void Validate(MediaShape shape, bool video)
    {
        if (shape.Width < 1 || shape.Height < 1 || shape.Width > 8192 || shape.Height > 8192 || (long)shape.Width * shape.Height > 20_000_000) throw Invalid();
        if (video && (shape.Width > 1920 || shape.Height > 1920 || (long)shape.Width * shape.Height > 2_073_600 || !double.IsFinite(shape.Duration) || shape.Duration <= 0 || shape.Duration > MaxVideoSeconds || !double.IsFinite(shape.Fps) || shape.Fps <= 0 || shape.Fps > 60 || shape.Codec is not ("h264" or "hevc" or "mpeg4" or "mjpeg" or "av1" or "vp9"))) throw Invalid();
        if (!video && (shape.Frames != 1 || shape.Codec is not ("png" or "mjpeg" or "webp"))) throw Invalid();
    }

    // No shell, remote URLs, unbounded logs, environment credentials, or forwarded client arguments.
    public static async Task<string> Run(string executable, IEnumerable<string> args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment.Clear();
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        try
        {
            var stdout = ReadBounded(process.StandardOutput, deadline.Token);
            var stderr = ReadBounded(process.StandardError, deadline.Token);
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).WaitAsync(deadline.Token);
            if (process.ExitCode != 0) throw Invalid();
            return await stdout;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[2048]; int read;
        while ((read = await reader.ReadAsync(buffer, token)) > 0)
        {
            if (result.Length + read > 16384) throw Invalid();
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
    private static MediaProcessingException Invalid() => new(400, "الملف تالف أو خارج حدود الفحص: صورة ثابتة حتى 20 مليون بكسل و8 ميجابايت، أو فيديو حتى دقيقتين و1080p و60 إطارًا/ثانية و50 ميجابايت. لم يُنشر الملف.");
    public void Dispose() => slots.Dispose();
    private sealed record MediaShape(int Width, int Height, double Duration, double Fps, long Frames, string Codec);
}

public sealed class MediaProcessingException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed class ProcessedMedia(string directory, string path, string contentType, long length) : IDisposable
{
    public string Path { get; } = path;
    public string ContentType { get; } = contentType;
    public long Length { get; } = length;
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
