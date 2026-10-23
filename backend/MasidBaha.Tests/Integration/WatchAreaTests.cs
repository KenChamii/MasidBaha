using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasidBaha.Application.Admin;
using MasidBaha.Application.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace MasidBaha.Tests.Integration;

// Watch areas: visitors choose places to be alerted about, and pushes only go to the people who care
[Collection(PostgisCollection.Name)]
public class WatchAreaTests : IAsyncLifetime
{
    private const string Api = "/api/v1";

    // Marikina and Cebu are ~570 km apart, so an area around one never covers the other
    private const double MarikinaLat = 14.6507, MarikinaLng = 121.1029;
    private const double CebuLat = 10.3157, CebuLng = 123.8854;

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public WatchAreaTests(PostgisFixture postgis) => _postgis = postgis;

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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A browser that has turned alerts on: one visitor, one push subscription
    private async Task<HttpClient> SubscribedVisitorAsync(string endpointName)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"{Api}/push/subscribe", new
        {
            endpoint = $"https://push.example.test/{endpointName}",
            keys = new { p256dh = "key-p256dh", auth = "key-auth" }
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> WatchAsync(HttpClient client, string name, double lat, double lng, int radius = 2000) =>
        client.PostAsJsonAsync($"{Api}/push/watch-areas", new { name, lat, lng, radiusMeters = radius });

    private async Task<List<string>> RecipientsAsync(double? lat, double? lng, int? radius)
    {
        using var scope = _factory.Services.CreateScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<IPushSubscriptionService>();
        return (await subscriptions.GetRecipientsAsync(lat, lng, radius)).Select(s => s.Endpoint[(s.Endpoint.LastIndexOf('/') + 1)..]).OrderBy(x => x).ToList();
    }

    // ---------- managing areas ----------

    [Fact]
    public async Task Listing_WithoutACookie_IsEmpty_AndDoesNotIssueOne()
    {
        var response = await _factory.CreateClient().GetAsync($"{Api}/push/watch-areas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await JsonAsync(response)).GetArrayLength());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task AnAreaNeedsAlertsToBeTurnedOnFirst()
    {
        var response = await WatchAsync(_factory.CreateClient(), "Home", MarikinaLat, MarikinaLng);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddListAndRemove()
    {
        var client = await SubscribedVisitorAsync("a");

        var created = await WatchAsync(client, "  Home  ", MarikinaLat, MarikinaLng, 3000);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var area = await JsonAsync(created);
        Assert.Equal("Home", area.GetProperty("name").GetString());
        Assert.Equal(3000, area.GetProperty("radiusMeters").GetInt32());
        Assert.Equal(MarikinaLat, area.GetProperty("lat").GetDouble(), 4);

        var list = await JsonAsync(await client.GetAsync($"{Api}/push/watch-areas"));
        Assert.Equal(1, list.GetArrayLength());

        var id = area.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Api}/push/watch-areas/{id}")).StatusCode);
        Assert.Equal(0, (await JsonAsync(await client.GetAsync($"{Api}/push/watch-areas"))).GetArrayLength());
    }

    [Fact]
    public async Task AVisitorCanWatchAtMostFivePlaces()
    {
        var client = await SubscribedVisitorAsync("a");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await WatchAsync(client, $"Place {i}", MarikinaLat + i * 0.01, MarikinaLng)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await WatchAsync(client, "One too many", MarikinaLat, MarikinaLng)).StatusCode);
    }

    [Theory]
    [InlineData("", 14.65, 121.10, 2000)]       // no name
    [InlineData("Tokyo", 35.68, 139.69, 2000)]  // outside the Philippines
    [InlineData("Tiny", 14.65, 121.10, 100)]    // radius too small
    [InlineData("Huge", 14.65, 121.10, 90000)]  // radius too large
    public async Task InvalidAreas_AreRejected(string name, double lat, double lng, int radius)
    {
        var client = await SubscribedVisitorAsync("a");

        Assert.Equal(HttpStatusCode.BadRequest, (await WatchAsync(client, name, lat, lng, radius)).StatusCode);
    }

    [Fact]
    public async Task OneVisitor_CannotSeeOrRemoveAnothersArea()
    {
        var owner = await SubscribedVisitorAsync("owner");
        var other = await SubscribedVisitorAsync("other");
        var id = (await JsonAsync(await WatchAsync(owner, "Home", MarikinaLat, MarikinaLng))).GetProperty("id").GetGuid();

        Assert.Equal(0, (await JsonAsync(await other.GetAsync($"{Api}/push/watch-areas"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Api}/push/watch-areas/{id}")).StatusCode);
        Assert.Equal(1, (await JsonAsync(await owner.GetAsync($"{Api}/push/watch-areas"))).GetArrayLength());
    }

    [Fact]
    public async Task TurningAlertsOff_RemovesTheAreas()
    {
        var client = await SubscribedVisitorAsync("a");
        await WatchAsync(client, "Home", MarikinaLat, MarikinaLng);

        var off = await client.PostAsJsonAsync($"{Api}/push/unsubscribe", new { endpoint = "https://push.example.test/a" });
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        Assert.Equal(0, (await JsonAsync(await client.GetAsync($"{Api}/push/watch-areas"))).GetArrayLength());
    }

    // ---------- who gets pushed ----------

    [Fact]
    public async Task ANewReport_ReachesWatchersOfThatSpot_AndPeopleWhoNeverChosePlaces()
    {
        var marikina = await SubscribedVisitorAsync("watches-marikina");
        var cebu = await SubscribedVisitorAsync("watches-cebu");
        await SubscribedVisitorAsync("watches-nothing");
        await WatchAsync(marikina, "Home", MarikinaLat, MarikinaLng, 5000);
        await WatchAsync(cebu, "Office", CebuLat, CebuLng, 5000);

        // A report in Marikina: the Marikina watcher, plus the person with no areas (they keep getting everything)
        Assert.Equal(["watches-marikina", "watches-nothing"], await RecipientsAsync(MarikinaLat, MarikinaLng, null));
        Assert.Equal(["watches-cebu", "watches-nothing"], await RecipientsAsync(CebuLat, CebuLng, null));
    }

    [Fact]
    public async Task AReportOutsideEveryArea_ReachesOnlyPeopleWithoutAreas()
    {
        var marikina = await SubscribedVisitorAsync("watches-marikina");
        await SubscribedVisitorAsync("watches-nothing");
        await WatchAsync(marikina, "Home", MarikinaLat, MarikinaLng, 1000);

        // About 5 km away: outside the 1 km area
        Assert.Equal(["watches-nothing"], await RecipientsAsync(MarikinaLat + 0.05, MarikinaLng, null));
    }

    [Fact]
    public async Task NoLocation_MeansEverybody()
    {
        var marikina = await SubscribedVisitorAsync("a");
        await SubscribedVisitorAsync("b");
        await WatchAsync(marikina, "Home", MarikinaLat, MarikinaLng);

        Assert.Equal(["a", "b"], await RecipientsAsync(null, null, null));
    }

    [Fact]
    public async Task AnAreaAnnouncement_ReachesOnlyPeopleWatchingInsideIt()
    {
        var marikina = await SubscribedVisitorAsync("watches-marikina");
        var cebu = await SubscribedVisitorAsync("watches-cebu");
        await SubscribedVisitorAsync("watches-nothing");
        await WatchAsync(marikina, "Home", MarikinaLat, MarikinaLng, 2000);
        await WatchAsync(cebu, "Office", CebuLat, CebuLng, 2000);

        // A circle over Metro Manila: only the Marikina watcher, and not the person with no areas
        Assert.Equal(["watches-marikina"], await RecipientsAsync(14.60, 121.00, 30_000));

        // The announcement circle and a watch area only need to overlap, not contain each other
        Assert.Equal(["watches-marikina"], await RecipientsAsync(MarikinaLat + 0.05, MarikinaLng, 4000));
    }

    // ---------- admin: area broadcasts ----------

    [Fact]
    public async Task Broadcast_WithAnArea_CountsOnlyWatchersThere()
    {
        var marikina = await SubscribedVisitorAsync("watches-marikina");
        var cebu = await SubscribedVisitorAsync("watches-cebu");
        await WatchAsync(marikina, "Home", MarikinaLat, MarikinaLng);
        await WatchAsync(cebu, "Office", CebuLat, CebuLng);
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);

        var everyone = await moderator.PostAsJsonAsync($"{Api}/admin/broadcasts", new { title = "All", body = "Everyone" });
        Assert.Equal(2, (await JsonAsync(everyone)).GetProperty("recipients").GetInt32());

        var metroManila = await moderator.PostAsJsonAsync($"{Api}/admin/broadcasts",
            new { title = "Typhoon", body = "Metro Manila", areaLat = 14.60, areaLng = 121.00, areaRadiusMeters = 30000 });
        var sent = await JsonAsync(metroManila);
        Assert.Equal(1, sent.GetProperty("recipients").GetInt32());
        Assert.Equal(30000, sent.GetProperty("areaRadiusMeters").GetInt32());
        Assert.Contains(_factory.Push.Enqueued, p => p.Title == "Typhoon" && p.RadiusMeters == 30000 && p.Lat == 14.60);
    }

    [Fact]
    public async Task Broadcast_Area_MustBeCompleteAndInsideThePhilippines()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);

        var partial = await moderator.PostAsJsonAsync($"{Api}/admin/broadcasts", new { title = "x", body = "y", areaLat = 14.6 });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        var abroad = await moderator.PostAsJsonAsync($"{Api}/admin/broadcasts",
            new { title = "x", body = "y", areaLat = 35.6, areaLng = 139.6, areaRadiusMeters = 5000 });
        Assert.Equal(HttpStatusCode.BadRequest, abroad.StatusCode);
    }
}
