using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace Academy.Api.Hosting;

// Storage never grants access. Callers must finish live tenant/resource authorization first.
public sealed class MediaObjectStore : IDisposable
{
    private readonly IAmazonS3? client;
    private readonly string root;
    private readonly string? bucket;
    public MediaObjectStore(IConfiguration config, IHostEnvironment env)
    {
        root = Path.GetFullPath(config["Media:PrivateRoot"] ?? Path.Combine(env.ContentRootPath, "local-uploads"));
        if (config["Media:Provider"] is not (null or "" or "Local" or "Spaces"))
            throw new InvalidOperationException("Unknown media provider.");
        if (config["Media:Provider"] != "Spaces") return;
        bucket = config["Spaces:Bucket"] ?? throw new InvalidOperationException("Missing private bucket configuration.");
        var endpoint = new Uri(config["Spaces:Endpoint"] ?? throw new InvalidOperationException("Missing Spaces endpoint."));
        if (endpoint.Scheme != "https" && !(env.IsEnvironment("Testing") && endpoint.IsLoopback))
            throw new InvalidOperationException("Spaces requires HTTPS.");
        if (string.IsNullOrWhiteSpace(config["Spaces:AccessKey"]) || string.IsNullOrWhiteSpace(config["Spaces:SecretKey"]))
            throw new InvalidOperationException("Missing private object credentials.");
        client = new AmazonS3Client(config["Spaces:AccessKey"], config["Spaces:SecretKey"], new AmazonS3Config {
            ServiceURL = endpoint.ToString(), AuthenticationRegion = config["Spaces:Region"], ForcePathStyle = true,
            RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(45), MaxErrorRetry = 2
        });
    }
    private string Local(string key) => Path.Combine(root, key[(key.LastIndexOf('/') + 1)..]);
    private static void Validate(string key)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "\\A(?:[a-f0-9]{32}|help)/[a-f0-9]{32}(?:\\.mp4|\\.png)?\\z"))
            throw new ArgumentException("Invalid private object key.");
    }
    public async Task Put(string key, Stream input, CancellationToken ct = default)
    {
        Validate(key);
        if (client != null) {
            await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = input,
                AutoCloseStream = false, CannedACL = S3CannedACL.Private, UseChunkEncoding = false }, ct);
            return;
        }
        var path = key.StartsWith("help/") ? Path.Combine(root, key) : Local(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = new FileStream(path, FileMode.CreateNew);
        await input.CopyToAsync(output, ct);
    }
    public async Task<Stream?> Open(string key, CancellationToken ct = default)
    {
        Validate(key);
        if (client == null) {
            try { return new FileStream(key.StartsWith("help/") ? Path.Combine(root,key) : Local(key), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
        }
        try {
            using var result = await client.GetObjectAsync(bucket, key, ct);
            // A request-local seekable spool preserves authorized video Range responses.
            // It is never the source of truth and disappears on dispose/container replacement.
            var stream = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try { await result.ResponseStream.CopyToAsync(stream, ct); stream.Position = 0; return stream; }
            catch { await stream.DisposeAsync(); throw; }
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async Task<bool> Exists(string key) { await using var stream = await Open(key); return stream != null; }
    public async Task Delete(string key)
    {
        Validate(key);
        if (client != null) await client.DeleteObjectAsync(bucket, key);
        else File.Delete(key.StartsWith("help/") ? Path.Combine(root,key) : Local(key));
    }
    public void Dispose() => client?.Dispose();
}
