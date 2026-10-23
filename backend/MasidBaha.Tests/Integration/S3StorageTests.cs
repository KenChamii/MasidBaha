using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.Photos.UploadPhoto;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MasidBaha.Tests.Integration;

// Real S3 API behaviour against moto (an S3-compatible server; R2 and Supabase Storage speak the same protocol)
[Collection(PostgisCollection.Name)]
public class S3StorageTests : IAsyncLifetime
{
    private const string Bucket = "masidbaha-photos";

    private readonly PostgisFixture _postgis;
    private const string AccessKey = "test", SecretKey = "test";

    private readonly IContainer _s3 = new ContainerBuilder("motoserver/moto:latest")
        .WithPortBinding(5000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(5000).ForPath("/moto-api/")))
        .Build();
    private string _endpoint = "";
    private AmazonS3Client _admin = null!;

    public S3StorageTests(PostgisFixture postgis) => _postgis = postgis;

    public async Task InitializeAsync()
    {
        await _s3.StartAsync();
        _endpoint = $"http://{_s3.Hostname}:{_s3.GetMappedPublicPort(5000)}";

        _admin = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = _endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await _admin.PutBucketAsync(Bucket);

        await _postgis.ResetAsync();
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _s3.DisposeAsync();
    }

    private S3StorageOptions S3Options(string? bucket = Bucket) => new()
    {
        ServiceUrl = _endpoint,
        Bucket = bucket,
        AccessKey = AccessKey,
        SecretKey = SecretKey,
        Region = "us-east-1",
        PublicBaseUrl = "https://photos.example.com"
    };

    private S3PhotoStorageService NewStorage(string? bucket = Bucket) =>
        new(Options.Create(new StorageOptions { Provider = "S3", S3 = S3Options(bucket) }));

    private static byte[] PngWithGps()
    {
        using var image = new Image<Rgba32>(64, 48, new Rgba32(10, 100, 200));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "TestCam");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.Metadata.ExifProfile = exif;

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task SaveAsync_WritesTheObject_AndReturnsItsPublicUrl()
    {
        using var storage = NewStorage();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        var url = await storage.SaveAsync(bytes, "abc.jpg", "image/jpeg");

        var key = $"uploads/{DateTime.UtcNow:yyyy/MM}/abc.jpg";
        Assert.Equal($"https://photos.example.com/{key}", url);

        using var stored = await _admin.GetObjectAsync(Bucket, key);
        using var buffer = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(buffer);
        Assert.Equal(bytes, buffer.ToArray());
        Assert.Equal("image/jpeg", stored.Headers.ContentType);
        Assert.Contains("immutable", stored.Headers.CacheControl);
    }

    [Fact]
    public async Task Upload_ReEncodesTheImage_AndStripsGpsMetadata()
    {
        using var storage = NewStorage();
        var service = new UploadPhotoService(storage, Options.Create(new PhotoOptions()));
        var original = PngWithGps();

        var url = await service.UploadAsync(new MemoryStream(original), "image/png", original.Length);

        var key = url.Replace("https://photos.example.com/", "");
        using var stored = await _admin.GetObjectAsync(Bucket, key);
        using var buffer = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(buffer);
        buffer.Position = 0;

        using var decoded = await Image.LoadAsync(buffer);
        Assert.Equal("JPEG", decoded.Metadata.DecodedImageFormat!.Name);
        Assert.Null(decoded.Metadata.ExifProfile);
    }

    [Fact]
    public async Task CheckAvailability_PassesForARealBucket_AndFailsForAMissingOne()
    {
        using var good = NewStorage();
        await good.CheckAvailabilityAsync();

        using var missing = NewStorage("no-such-bucket");
        await Assert.ThrowsAnyAsync<AmazonS3Exception>(() => missing.CheckAvailabilityAsync());
    }

    [Theory]
    [InlineData(null, "bucket", "a", "s", "https://x")]
    [InlineData("http://s3", null, "a", "s", "https://x")]
    [InlineData("http://s3", "bucket", "", "s", "https://x")]
    [InlineData("http://s3", "bucket", "a", null, "https://x")]
    [InlineData("http://s3", "bucket", "a", "s", null)]
    public void IncompleteConfiguration_IsRejectedWithAHelpfulMessage(string? url, string? bucket, string? key, string? secret, string? publicUrl)
    {
        var options = new StorageOptions
        {
            Provider = "S3",
            S3 = new S3StorageOptions { ServiceUrl = url, Bucket = bucket, AccessKey = key, SecretKey = secret, PublicBaseUrl = publicUrl }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new S3PhotoStorageService(Options.Create(options)));

        Assert.Contains("Storage:S3", ex.Message);
    }

    [Fact]
    public async Task TheWholeApi_WorksWithS3Selected()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new()
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:ServiceUrl"] = _endpoint,
            ["Storage:S3:Bucket"] = Bucket,
            ["Storage:S3:AccessKey"] = AccessKey,
            ["Storage:S3:SecretKey"] = SecretKey,
            ["Storage:S3:Region"] = "us-east-1",
            ["Storage:S3:PublicBaseUrl"] = "https://photos.example.com"
        }, fakeStorage: false);
        var client = factory.CreateClient();

        // readiness reflects the bucket
        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready");
        Assert.Equal("Healthy", ready.GetProperty("checks").GetProperty("storage").GetString());

        // upload goes to the bucket and the URL is the public bucket domain, not this API
        var png = new ByteArrayContent(PngWithGps());
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { png, "file", "flood.png" } };
        var upload = await client.PostAsync("/api/v1/photos", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var url = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        Assert.StartsWith("https://photos.example.com/uploads/", url);

        // a report may use that URL, but not a local-style or foreign one
        var ok = await client.PostAsJsonAsync("/api/v1/flood-reports", new { lat = 14.65, lng = 121.10, severity = 2, photoUrl = url });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var foreign = await client.PostAsJsonAsync("/api/v1/flood-reports",
            new { lat = 14.65, lng = 121.10, severity = 2, photoUrl = "http://localhost/uploads/x.jpg" });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReportsUnhealthy_WhenTheBucketIsUnreachable()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new()
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:ServiceUrl"] = _endpoint,
            ["Storage:S3:Bucket"] = "no-such-bucket",
            ["Storage:S3:AccessKey"] = AccessKey,
            ["Storage:S3:SecretKey"] = SecretKey,
            ["Storage:S3:Region"] = "us-east-1",
            ["Storage:S3:PublicBaseUrl"] = "https://photos.example.com"
        }, fakeStorage: false);

        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unhealthy", body.GetProperty("checks").GetProperty("storage").GetString());
    }
}
