using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Common.Storage;

// Photo storage on any S3-compatible service: Cloudflare R2, Supabase Storage,
// MinIO or AWS S3. Free hosts have ephemeral disks, so local files would
// vanish on every redeploy.
public class S3PhotoStorageService : IPhotoStorageService, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly S3StorageOptions _options;

    public S3PhotoStorageService(IOptions<StorageOptions> options)
    {
        _options = options.Value.S3;

        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) ||
            string.IsNullOrWhiteSpace(_options.Bucket) ||
            string.IsNullOrWhiteSpace(_options.AccessKey) ||
            string.IsNullOrWhiteSpace(_options.SecretKey) ||
            string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            throw new InvalidOperationException(
                "Storage:Provider is S3 but Storage:S3 is incomplete. Set ServiceUrl, Bucket, AccessKey, SecretKey and PublicBaseUrl.");
        }

        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                AuthenticationRegion = _options.Region,
                ForcePathStyle = true,
                // R2, MinIO and Supabase reject the extra checksum headers newer SDKs add by default
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
    }

    public async Task<string> SaveAsync(byte[] content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var key = $"uploads/{DateTime.UtcNow:yyyy/MM}/{fileName}";

        using var stream = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            // File names are random GUIDs, so a stored photo never changes
            Headers = { CacheControl = "public, max-age=31536000, immutable" }
        }, cancellationToken);

        // Absolute URL: the bucket is served from its own public domain
        return $"{_options.PublicBaseUrl!.TrimEnd('/')}/{key}";
    }

    // Cheap reachability probe for the readiness health check (lists at most one key)
    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        await _client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket, MaxKeys = 1 }, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
