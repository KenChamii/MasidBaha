using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Common.Storage;
using Microsoft.Extensions.Options;

namespace MasidBaha.Tests;

public class PhotoUrlPolicyTests
{
    private static PhotoUrlPolicy Local() => new(Options.Create(new StorageOptions { Provider = "Local" }));

    private static PhotoUrlPolicy S3() => new(Options.Create(new StorageOptions
    {
        Provider = "S3",
        S3 = new S3StorageOptions { PublicBaseUrl = "https://photos.example.com" }
    }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoPhoto_IsAllowed(string? url)
    {
        Assert.True(Local().IsOwnUpload(url, "api.example.com"));
        Assert.True(S3().IsOwnUpload(url, "api.example.com"));
    }

    [Theory]
    [InlineData("https://api.example.com/uploads/2026-10/abc.jpg")]
    [InlineData("http://API.example.com/uploads/abc.webp")]
    public void Local_OwnUpload_IsAllowed(string url)
    {
        Assert.True(Local().IsOwnUpload(url, "api.example.com"));
    }

    [Theory]
    [InlineData("https://evil.example.net/uploads/abc.jpg")]   // other host
    [InlineData("https://api.example.com/static/abc.jpg")]     // not the uploads folder
    [InlineData("/uploads/abc.jpg")]                           // relative
    [InlineData("javascript:alert(1)")]                        // wrong scheme
    [InlineData("ftp://api.example.com/uploads/abc.jpg")]
    public void Local_Foreign_IsRejected(string url)
    {
        Assert.False(Local().IsOwnUpload(url, "api.example.com"));
    }

    [Fact]
    public void S3_AcceptsOnlyTheConfiguredBucketDomain()
    {
        var policy = S3();

        Assert.True(policy.IsOwnUpload("https://photos.example.com/uploads/2026/10/a.jpg", "api.example.com"));
        Assert.False(policy.IsOwnUpload("https://photos.example.com.evil.net/uploads/a.jpg", "api.example.com"));
        Assert.False(policy.IsOwnUpload("https://api.example.com/uploads/a.jpg", "api.example.com")); // local path no longer valid
        Assert.False(policy.IsOwnUpload("https://photos.example.com/other/a.jpg", "api.example.com"));
    }

    [Fact]
    public void ToPublicUrl_PrefixesLocalPaths_AndKeepsAbsoluteUrls()
    {
        Assert.Equal("https://api.example.com/uploads/2026/10/a.jpg",
            Local().ToPublicUrl("/uploads/2026/10/a.jpg", "https://api.example.com/"));

        Assert.Equal("https://photos.example.com/uploads/a.jpg",
            S3().ToPublicUrl("https://photos.example.com/uploads/a.jpg", "https://api.example.com"));
    }
}
