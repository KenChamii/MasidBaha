using MasidBaha.WebAPI.Controllers;

namespace MasidBaha.Tests;

public class PhotoUrlValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoPhoto_IsAllowed(string? url)
    {
        Assert.True(PhotoUrlValidator.IsOwnUpload(url, "api.example.com"));
    }

    [Theory]
    [InlineData("https://api.example.com/uploads/2026-10/abc.jpg")]
    [InlineData("http://API.example.com/uploads/abc.webp")]
    public void OwnUpload_IsAllowed(string url)
    {
        Assert.True(PhotoUrlValidator.IsOwnUpload(url, "api.example.com"));
    }

    [Theory]
    [InlineData("https://evil.example.net/uploads/abc.jpg")]   // other host
    [InlineData("https://api.example.com/static/abc.jpg")]     // not the uploads folder
    [InlineData("/uploads/abc.jpg")]                           // relative
    [InlineData("javascript:alert(1)")]                        // wrong scheme
    [InlineData("ftp://api.example.com/uploads/abc.jpg")]
    public void Foreign_IsRejected(string url)
    {
        Assert.False(PhotoUrlValidator.IsOwnUpload(url, "api.example.com"));
    }
}
