using System.Net;
using System.Net.Http.Json;

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
}
