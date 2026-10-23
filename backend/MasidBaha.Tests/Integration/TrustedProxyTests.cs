using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MasidBaha.Tests.Integration;

// The Cloudflare proxy may tell the API who the visitor is, but only with the shared secret
[Collection(PostgisCollection.Name)]
public class TrustedProxyTests : IAsyncLifetime
{
    private const string Secret = "proxy-secret-for-tests-0123456789";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public TrustedProxyTests(PostgisFixture postgis) => _postgis = postgis;

    public async Task InitializeAsync()
    {
        await _postgis.ResetAsync();
        // One new report per minute per address, so a second one from the same address is refused
        _factory = new ApiFactory(_postgis.ConnectionString, new Dictionary<string, string?>
        {
            ["Security:TrustedProxySecret"] = Secret,
            ["RateLimits:ReportWrites:PermitLimit"] = "1"
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<HttpStatusCode> PostReportAsync(HttpClient client, string? secret, string? clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/flood-reports")
        {
            Content = JsonContent.Create(new { lat = 14.65, lng = 121.10, severity = 2 })
        };
        if (secret is not null) request.Headers.Add("X-Proxy-Secret", secret);
        if (clientIp is not null) request.Headers.Add("X-Client-IP", clientIp);

        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task WithTheSecret_EachVisitorGetsTheirOwnRateLimit()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, await PostReportAsync(client, Secret, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.Created, await PostReportAsync(client, Secret, "203.0.113.11"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostReportAsync(client, Secret, "203.0.113.10"));
    }

    [Fact]
    public async Task WithoutTheRightSecret_TheClaimedAddressIsIgnored()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, await PostReportAsync(client, null, "203.0.113.10"));
        // Same real source: a different claimed address, with no secret or a wrong one, buys nothing
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostReportAsync(client, null, "203.0.113.11"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostReportAsync(client, "wrong-secret-wrong-secret-0000", "203.0.113.12"));
    }

    // Behind the proxy the API's own host is not the address visitors use, so the proxy forwards theirs
    [Fact]
    public async Task Photos_AreLinkedOnTheSiteHostTheProxyForwards_AndAcceptedWhenAttached()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new Dictionary<string, string?>
        {
            ["Security:TrustedProxySecret"] = Secret,
            ["Security:TrustForwardedHeaders"] = "true"
        });
        var client = factory.CreateClient();

        using var image = new Image<Rgba32>(40, 30, new Rgba32(30, 120, 200));
        using var png = new MemoryStream();
        image.SaveAsPng(png);
        var file = new ByteArrayContent(png.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { file, "file", "flood.png" } };

        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photos") { Content = form };
        upload.Headers.Add("X-Forwarded-Host", "site.example");
        upload.Headers.Add("X-Forwarded-Proto", "https");
        var uploaded = await client.SendAsync(upload);

        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var url = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync()).RootElement.GetProperty("url").GetString()!;
        Assert.StartsWith("https://site.example/uploads/", url);

        var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/flood-reports")
        {
            Content = JsonContent.Create(new { lat = 14.65, lng = 121.10, severity = 2, photoUrl = url })
        };
        create.Headers.Add("X-Forwarded-Host", "site.example");
        create.Headers.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);
    }
}
