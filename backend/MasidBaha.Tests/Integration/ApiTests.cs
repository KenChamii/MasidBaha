using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MasidBaha.Application.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MasidBaha.Tests.Integration;

// Drives the real HTTP pipeline. Each HttpClient keeps its own cookies, so one
// client is one browser: two clients are two different anonymous visitors.
[Collection(PostgisCollection.Name)]
public class ApiTests : IAsyncLifetime
{
    private const string Api = "/api/v1";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public ApiTests(PostgisFixture postgis) => _postgis = postgis;

    public async Task InitializeAsync()
    {
        await _postgis.ResetAsync();
        _factory = new ApiFactory(_postgis.ConnectionString);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static object NewReport(double lat = 14.65, double lng = 121.10, int severity = 2, string? photoUrl = null) =>
        new { lat, lng, severity, notes = "Water is rising", photoUrl };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> CreateReportAsync(HttpClient client, int severity = 2, double lat = 14.65)
    {
        var response = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport(lat: lat, severity: severity));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    // ---------- versioning and docs ----------

    [Fact]
    public async Task V1Routes_Work_AndUnversionedRoutesAreGone()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Api}/flood-reports/top")).StatusCode);

        var legacy = await client.GetAsync("/api/flood-reports/top");
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsPublic_AndDescribesVersionedPaths()
    {
        var client = _factory.CreateClient();

        var doc = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        var json = await doc.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/flood-reports", json);
        Assert.Contains("/api/v1/admin/reports", json);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/docs")).StatusCode);
    }

    // ---------- create ----------

    [Fact]
    public async Task Create_Returns201_WithLocation_Body_AndAnHttpOnlySessionCookie()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.EndsWith($"{Api}/flood-reports/{id}", response.Headers.Location!.ToString());
        Assert.Equal(1, body.GetProperty("confidenceScore").GetInt32());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("mb_session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        // the Location header actually resolves
        var fetched = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(id, (await JsonAsync(fetched)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Create_OutsideThePhilippines_Is400ProblemDetails()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport(lat: 35.68, lng: 139.69));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await JsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Lat", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("Lng", out _));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Create_WithAnInvalidSeverity_Is400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports", NewReport(severity: 9));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("Severity", out _));
    }

    [Fact]
    public async Task Create_TheReporterIsTheCookieHolder_NotWhoeverTheBodyClaims()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Api}/flood-reports",
            new { lat = 14.65, lng = 121.10, severity = 2, reporterSessionId = "forged-session" });
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();

        await using var db = _postgis.CreateDbContext();
        var stored = await db.FloodReports.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.NotEqual("forged-session", stored.ReporterSessionId);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains(stored.ReporterSessionId, cookie);
    }

    [Fact]
    public async Task Create_WithAForeignPhotoUrl_IsRejected()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"{Api}/flood-reports", NewReport(photoUrl: "https://evil.example.net/uploads/x.jpg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("PhotoUrl", out _));
    }

    [Fact]
    public async Task GetById_ForAnUnknownReport_Is404ProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync($"{Api}/flood-reports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---------- reads ----------

    [Fact]
    public async Task Nearby_RejectsOutOfRangeInput_WithProblemDetails()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{Api}/flood-reports?lat=14.65&lng=121.10&radiusMeters=999999999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("RadiusMeters", out _));
    }

    [Fact]
    public async Task Nearby_FindsAReportJustCreated()
    {
        var client = _factory.CreateClient();
        await CreateReportAsync(client);

        var nearby = await client.GetFromJsonAsync<JsonElement>($"{Api}/flood-reports?lat=14.65&lng=121.10&radiusMeters=5000");

        Assert.Equal(1, nearby.GetArrayLength());
    }

    // ---------- voting ----------

    [Fact]
    public async Task Vote_OneBrowserCannotVoteTwice_EvenByChangingTheIdInTheBody()
    {
        var owner = _factory.CreateClient();
        var id = await CreateReportAsync(owner);
        var voter = _factory.CreateClient();

        var first = await voter.PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voterSessionId = "id-one", voteType = 1 });
        var second = await voter.PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voterSessionId = "id-two", voteType = 1 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType!.MediaType);
        Assert.Equal(409, (await JsonAsync(second)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Vote_ADifferentBrowserCanVote()
    {
        var id = await CreateReportAsync(_factory.CreateClient());

        var a = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voteType = 1 });
        var b = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voteType = 1 });

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.Equal(3, (await JsonAsync(b)).GetProperty("confidenceScore").GetInt32());
    }

    [Fact]
    public async Task Vote_Deny_ResolvesTheReport()
    {
        var id = await CreateReportAsync(_factory.CreateClient());

        var response = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voteType = -1 });

        var result = await JsonAsync(response);
        Assert.Equal(0, result.GetProperty("confidenceScore").GetInt32());
        Assert.Equal((int)ReportStatus.Resolved, result.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Vote_OnAMissingReport_Is404_AndAnInvalidTypeIs400()
    {
        var client = _factory.CreateClient();

        var missing = await client.PostAsJsonAsync($"{Api}/flood-reports/{Guid.NewGuid()}/vote", new { voteType = 1 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var id = await CreateReportAsync(client);
        var invalid = await client.PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voteType = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await JsonAsync(invalid)).GetProperty("errors").TryGetProperty("VoteType", out _));
    }

    // ---------- cache ----------

    [Fact]
    public async Task TopReports_AreCachedBriefly_AndWritesEvictTheCache()
    {
        var client = _factory.CreateClient();
        var url = $"{Api}/flood-reports/top?limit=10";

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>(url)).GetArrayLength());

        // A report inserted behind the API's back isn't visible while the response is cached...
        await InsertReportDirectlyAsync();
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>(url)).GetArrayLength());

        // ...but any write through the API evicts the cache, so everyone sees both reports at once
        await CreateReportAsync(client, lat: 14.70);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>(url)).GetArrayLength());
    }

    private async Task InsertReportDirectlyAsync()
    {
        await using var db = _postgis.CreateDbContext();
        db.FloodReports.Add(new MasidBaha.Application.Persistence.FloodReport
        {
            Id = Guid.NewGuid(),
            Location = MasidBaha.Application.Persistence.FloodReportMappings.ToPoint(14.6, 121.0),
            Severity = Severity.KneeLevel,
            ReportedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(6),
            ReporterSessionId = "direct"
        });
        await db.SaveChangesAsync();
    }

    // ---------- push ----------

    [Theory]
    [InlineData(1, false)] // Passable: shows on the map but doesn't buzz phones
    [InlineData(2, true)]
    [InlineData(4, true)]
    public async Task Push_IsQueuedOnlyFromTheMinimumSeverity(int severity, bool expectPush)
    {
        _factory.Push.Enqueued.Clear();

        await CreateReportAsync(_factory.CreateClient(), severity);

        Assert.Equal(expectPush ? 1 : 0, _factory.Push.Enqueued.Count);
        if (expectPush) Assert.Contains("Marikina", _factory.Push.Enqueued[0].Body);
    }

    [Fact]
    public async Task PushSubscribe_ValidatesTheEndpoint_AndStoresTheCookieSession()
    {
        var client = _factory.CreateClient();

        var bad = await client.PostAsJsonAsync($"{Api}/push/subscribe",
            new { endpoint = "http://insecure.example.com", keys = new { p256dh = "k", auth = "a" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await client.PostAsJsonAsync($"{Api}/push/subscribe",
            new { sessionId = "forged", endpoint = "https://fcm.googleapis.com/fcm/send/abc", keys = new { p256dh = "k", auth = "a" } });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        await using var db = _postgis.CreateDbContext();
        var sub = await db.PushSubscriptions.AsNoTracking().SingleAsync();
        Assert.NotEqual("forged", sub.SessionId);
    }

    [Fact]
    public async Task VapidKey_Is404ProblemDetails_WhenPushIsNotConfigured()
    {
        var response = await _factory.CreateClient().GetAsync($"{Api}/push/vapid-public-key");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---------- admin ----------

    [Fact]
    public async Task Admin_RequiresTheKey()
    {
        var client = _factory.CreateClient();

        var none = await client.GetAsync($"{Api}/admin/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("application/problem+json", none.Content.Headers.ContentType!.MediaType);

        client.DefaultRequestHeaders.Add("X-Admin-Key", "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Api}/admin/reports")).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Admin-Key");
        client.DefaultRequestHeaders.Add("X-Admin-Key", ApiFactory.AdminKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Api}/admin/reports")).StatusCode);
    }

    [Fact]
    public async Task Admin_IsDisabledWhenNoKeyIsConfigured()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["Admin:ApiKey"] = "" });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "anything");

        var response = await client.GetAsync($"{Api}/admin/reports");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Moderation_UpdatesAndEvictsTheCache()
    {
        var reporter = _factory.CreateClient();
        var id = await CreateReportAsync(reporter);
        var topUrl = $"{Api}/flood-reports/top";
        Assert.Equal(1, (await reporter.GetFromJsonAsync<JsonElement>(topUrl)).GetArrayLength()); // now cached

        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Key", ApiFactory.AdminKey);

        var resolve = await admin.PatchAsJsonAsync($"{Api}/admin/reports/{id}/status", new { status = (int)ReportStatus.Resolved });
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
        Assert.Equal(0, (await reporter.GetFromJsonAsync<JsonElement>(topUrl)).GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Api}/admin/reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Api}/admin/reports/{id}")).StatusCode);

        var badStatus = await admin.PatchAsJsonAsync($"{Api}/admin/reports/{id}/status", new { status = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    // ---------- security, CORS, health ----------

    [Fact]
    public async Task Responses_CarryHardeningHeaders()
    {
        var response = await _factory.CreateClient().GetAsync($"{Api}/flood-reports/top");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Cors_AllowsTheFrontendOrigin_WithCredentials()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"{Api}/flood-reports");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var stranger = new HttpRequestMessage(HttpMethod.Options, $"{Api}/flood-reports");
        stranger.Headers.Add("Origin", "https://evil.example.net");
        stranger.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.False((await _factory.CreateClient().SendAsync(stranger)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Health_LivenessAndReadiness()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = await JsonAsync(ready);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("storage").GetString());
    }

    [Fact]
    public async Task UnknownRoutes_AreProblemDetailsToo()
    {
        var response = await _factory.CreateClient().GetAsync("/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---------- rate limiting ----------

    [Fact]
    public async Task RateLimit_ComesFromConfiguration_AndRepliesWith429AndRetryAfter()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["RateLimits:ReportWrites:PermitLimit"] = "2" });
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport())).StatusCode);

        var limited = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
    }

    // ---------- photos ----------

    private static ByteArrayContent PngUpload(out string fileName)
    {
        using var image = new Image<Rgba32>(40, 30, new Rgba32(30, 120, 200));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);

        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fileName = "flood.png";
        return content;
    }

    [Fact]
    public async Task Photo_Upload_ThenAttachToAReport()
    {
        var client = _factory.CreateClient();

        using var form = new MultipartFormDataContent { { PngUpload(out var name), "file", name } };
        var upload = await client.PostAsync($"{Api}/photos", form);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var url = (await JsonAsync(upload)).GetProperty("url").GetString()!;
        Assert.Contains("/uploads/", url);
        Assert.EndsWith(".jpg", url); // always re-encoded, which also strips metadata

        var create = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport(photoUrl: url));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(url, (await JsonAsync(create)).GetProperty("photoUrl").GetString());
    }

    [Fact]
    public async Task Photo_Upload_RejectsAFileThatIsNotAnImage()
    {
        var fake = new ByteArrayContent("definitely not an image"u8.ToArray());
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { fake, "file", "evil.png" } };

        var response = await _factory.CreateClient().PostAsync($"{Api}/photos", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---------- startup validation ----------

    [Fact]
    public void BadOptions_FailTheBootInsteadOfARequest()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["Reports:LifetimeHours"] = "0" });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("LifetimeHours", ex.ToString());
    }

    [Fact]
    public void MissingSigningKey_FailsTheBootOutsideDevelopment()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString,
            new() { ["Security:SessionSigningKey"] = "" }, environment: "Production");

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("SessionSigningKey", ex.ToString());
    }
}

internal static class HttpClientPatchExtensions
{
    public static Task<HttpResponseMessage> PatchAsJsonAsync<T>(this HttpClient client, string url, T value) =>
        client.PatchAsync(url, JsonContent.Create(value));
}
