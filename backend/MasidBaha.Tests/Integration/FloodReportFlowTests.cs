using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.PushNotifications;
using MasidBaha.Application.Trust;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasidBaha.Tests.Integration;

[Collection(PostgisCollection.Name)]
public class FloodReportFlowTests : IAsyncLifetime
{
    // Marikina and Cebu are ~570 km apart, so they never share a 5 km radius
    private const double MarikinaLat = 14.6507, MarikinaLng = 121.1029;
    private const double CebuLat = 10.3157, CebuLng = 123.8854;

    private readonly PostgisFixture _postgis;

    public FloodReportFlowTests(PostgisFixture postgis) => _postgis = postgis;

    public Task InitializeAsync() => _postgis.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private class FakeGeocoder : IGeocodingService
    {
        public Task<GeocodeResult> ReverseGeocodeAsync(double lat, double lng) =>
            Task.FromResult(new GeocodeResult { Region = "NCR", Province = "Metro Manila", City = "Marikina" });
    }

    private async Task<FloodReportDto> CreateAsync(double lat, double lng, Severity severity = Severity.KneeLevel, string session = "reporter-1")
    {
        await using var db = _postgis.CreateDbContext();
        return await new CreateFloodReportService(db, new FakeGeocoder(), Options.Create(new ReportOptions())).CreateAsync(new CreateFloodReportRequest
        {
            Lat = lat, Lng = lng, Severity = severity, ReporterSessionId = session
        });
    }

    private async Task<VoteResultDto> VoteAsync(Guid reportId, VoteType type, string session)
    {
        await using var db = _postgis.CreateDbContext();
        return await new VoteOnReportService(db).VoteAsync(reportId, new VoteRequest { VoterSessionId = session, VoteType = type });
    }

    private async Task SetExpiryAsync(Guid reportId, DateTime expiresAt)
    {
        await using var db = _postgis.CreateDbContext();
        await db.FloodReports.Where(r => r.Id == reportId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, expiresAt));
    }

    // ---------- create ----------

    [Fact]
    public async Task Create_RoundTripsCoordinatesAndStoresUtcTimestamps()
    {
        var created = await CreateAsync(MarikinaLat, MarikinaLng);

        Assert.Equal(MarikinaLat, created.Lat, 6);
        Assert.Equal(MarikinaLng, created.Lng, 6);
        Assert.Equal(DateTimeKind.Utc, created.ReportedAt.Kind);
        Assert.Equal(1, created.ConfidenceScore);

        await using var db = _postgis.CreateDbContext();
        var stored = await db.FloodReports.AsNoTracking().SingleAsync();
        Assert.Equal(MarikinaLng, stored.Location.X, 6); // longitude is X in PostGIS
        Assert.Equal(MarikinaLat, stored.Location.Y, 6);
        Assert.Equal(4326, stored.Location.SRID);
        Assert.InRange(stored.ExpiresAt - stored.ReportedAt, TimeSpan.FromHours(5.99), TimeSpan.FromHours(6.01));
    }

    // ---------- spatial queries ----------

    [Fact]
    public async Task Nearby_ReturnsOnlyReportsWithinRadius()
    {
        await CreateAsync(MarikinaLat, MarikinaLng);
        await CreateAsync(CebuLat, CebuLng);

        await using var db = _postgis.CreateDbContext();
        var nearby = await new GetNearbyReportsService(db)
            .GetNearbyAsync(new NearbyReportsQuery { Lat = MarikinaLat, Lng = MarikinaLng, RadiusMeters = 5000 });

        var only = Assert.Single(nearby);
        Assert.Equal(MarikinaLat, only.Lat, 6);
    }

    [Fact]
    public async Task Nearby_RadiusIsInMeters()
    {
        await CreateAsync(MarikinaLat, MarikinaLng);
        await CreateAsync(MarikinaLat + 0.045, MarikinaLng); // ~5 km north

        await using var db = _postgis.CreateDbContext();
        var service = new GetNearbyReportsService(db);

        var within2km = await service.GetNearbyAsync(new NearbyReportsQuery { Lat = MarikinaLat, Lng = MarikinaLng, RadiusMeters = 2000 });
        var within10km = await service.GetNearbyAsync(new NearbyReportsQuery { Lat = MarikinaLat, Lng = MarikinaLng, RadiusMeters = 10000 });

        Assert.Single(within2km);
        Assert.Equal(2, within10km.Count);
    }

    [Fact]
    public async Task Nearby_HidesReportsPastExpiry_EvenBeforeTheSweeperRuns()
    {
        var created = await CreateAsync(MarikinaLat, MarikinaLng);
        await SetExpiryAsync(created.Id, DateTime.UtcNow.AddMinutes(-1)); // status is still Active

        await using var db = _postgis.CreateDbContext();
        var nearby = await new GetNearbyReportsService(db)
            .GetNearbyAsync(new NearbyReportsQuery { Lat = MarikinaLat, Lng = MarikinaLng, RadiusMeters = 5000 });

        Assert.Empty(nearby);
    }

    [Fact]
    public async Task Top_OrdersByConfidenceAndSkipsExpired()
    {
        var low = await CreateAsync(MarikinaLat, MarikinaLng);
        var high = await CreateAsync(MarikinaLat + 0.01, MarikinaLng);
        var expired = await CreateAsync(MarikinaLat + 0.02, MarikinaLng);
        await VoteAsync(high.Id, VoteType.Confirm, "v1");
        await VoteAsync(high.Id, VoteType.Confirm, "v2");
        await SetExpiryAsync(expired.Id, DateTime.UtcNow.AddMinutes(-1));

        await using var db = _postgis.CreateDbContext();
        var top = await new GetTopReportsService(db, Options.Create(new ReportOptions())).GetTopAsync(new TopReportsQuery { Limit = 10 });

        Assert.Equal(new[] { high.Id, low.Id }, top.Select(r => r.Id));
        Assert.Equal(3, top[0].ConfidenceScore);
    }

    [Fact]
    public async Task Top_FiltersByCity()
    {
        await CreateAsync(MarikinaLat, MarikinaLng);

        await using var db = _postgis.CreateDbContext();
        var service = new GetTopReportsService(db, Options.Create(new ReportOptions()));

        Assert.Single(await service.GetTopAsync(new TopReportsQuery { City = "Marikina" }));
        Assert.Empty(await service.GetTopAsync(new TopReportsQuery { City = "Cebu City" }));
    }

    // ---------- voting ----------

    [Fact]
    public async Task Vote_Confirm_RaisesScoreByOne()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);

        var result = await VoteAsync(report.Id, VoteType.Confirm, "voter-1");

        Assert.Equal(2, result.ConfidenceScore);
        Assert.Equal(ReportStatus.Active, result.Status);
    }

    [Fact]
    public async Task Vote_Deny_LowersScoreAndResolves_NotPlus255()
    {
        // Regression: Deny (-1) used to be cast to byte (255) and ADD 255 to the score
        var report = await CreateAsync(MarikinaLat, MarikinaLng);

        var result = await VoteAsync(report.Id, VoteType.Deny, "voter-1");

        Assert.Equal(0, result.ConfidenceScore);
        Assert.Equal(ReportStatus.Resolved, result.Status);
    }

    [Fact]
    public async Task Vote_Twice_FromSameSession_IsAConflict()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);
        await VoteAsync(report.Id, VoteType.Confirm, "voter-1");

        await Assert.ThrowsAsync<ConflictException>(() => VoteAsync(report.Id, VoteType.Confirm, "voter-1"));
    }

    [Fact]
    public async Task Vote_OnMissingReport_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => VoteAsync(Guid.NewGuid(), VoteType.Confirm, "voter-1"));
    }

    [Fact]
    public async Task Vote_OnResolvedReport_IsAConflict()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);
        await VoteAsync(report.Id, VoteType.Deny, "voter-1"); // resolves it

        await Assert.ThrowsAsync<ConflictException>(() => VoteAsync(report.Id, VoteType.Confirm, "voter-2"));
    }

    [Fact]
    public async Task Vote_OnExpiredReport_IsAConflict()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);
        await SetExpiryAsync(report.Id, DateTime.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAsync<ConflictException>(() => VoteAsync(report.Id, VoteType.Confirm, "voter-1"));
    }

    [Fact]
    public async Task Vote_ConcurrentVotes_AreNotLost()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);

        // 10 different sessions vote at once; the row lock must serialize them
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => VoteAsync(report.Id, VoteType.Confirm, $"voter-{i}")));

        await using var db = _postgis.CreateDbContext();
        var stored = await db.FloodReports.AsNoTracking().SingleAsync();
        Assert.Equal(11, stored.ConfidenceScore);
        Assert.Equal(10, await db.ReportVotes.CountAsync());
    }

    [Fact]
    public async Task Vote_ConcurrentDuplicatesFromOneSession_OnlyOneSucceeds()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { await VoteAsync(report.Id, VoteType.Confirm, "same-voter"); return true; }
            catch (ConflictException) { return false; }
        }));

        Assert.Equal(1, attempts.Count(ok => ok));
        await using var db = _postgis.CreateDbContext();
        Assert.Equal(2, (await db.FloodReports.AsNoTracking().SingleAsync()).ConfidenceScore);
    }

    // ---------- expiry sweep ----------

    [Fact]
    public async Task ExpireSweep_MarksOnlyStaleActiveReportsAndReturnsTheirIds()
    {
        var stale = await CreateAsync(MarikinaLat, MarikinaLng);
        var fresh = await CreateAsync(MarikinaLat + 0.01, MarikinaLng);
        await SetExpiryAsync(stale.Id, DateTime.UtcNow.AddMinutes(-1));

        await using var db = _postgis.CreateDbContext();
        var expiredIds = await new ExpireReportsService(db).ExpireStaleReportsAsync();

        Assert.Equal(new[] { stale.Id }, expiredIds);
        await using var verify = _postgis.CreateDbContext();
        Assert.Equal(ReportStatus.Expired, (await verify.FloodReports.FindAsync(stale.Id))!.Status);
        Assert.Equal(ReportStatus.Active, (await verify.FloodReports.FindAsync(fresh.Id))!.Status);

        // a second sweep has nothing left to do
        Assert.Empty(await new ExpireReportsService(verify).ExpireStaleReportsAsync());
    }

    // ---------- heatmap ----------

    [Fact]
    public async Task Heatmap_IncludesClosedReports_AndHonoursTheDateRange()
    {
        var recent = await CreateAsync(MarikinaLat, MarikinaLng);
        var old = await CreateAsync(MarikinaLat + 0.01, MarikinaLng);
        await VoteAsync(recent.Id, VoteType.Deny, "v1"); // resolved, still historical data

        await using (var db = _postgis.CreateDbContext())
        {
            await db.FloodReports.Where(r => r.Id == old.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReportedAt, DateTime.UtcNow.AddMonths(-8)));
        }

        await using var query = _postgis.CreateDbContext();
        var service = new GetHeatmapDataService(query);

        var defaultRange = await service.GetHeatmapAsync(new HeatmapQuery()); // last 6 months
        Assert.Single(defaultRange);
        Assert.Equal(ReportStatus.Resolved, defaultRange[0].Status);

        var wide = await service.GetHeatmapAsync(new HeatmapQuery { FromDate = DateTime.UtcNow.AddYears(-1) });
        Assert.Equal(2, wide.Count);
    }

    [Fact]
    public async Task Heatmap_AcceptsUnspecifiedKindDates()
    {
        await CreateAsync(MarikinaLat, MarikinaLng);
        var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Unspecified);

        await using var db = _postgis.CreateDbContext();
        var points = await new GetHeatmapDataService(db).GetHeatmapAsync(new HeatmapQuery { FromDate = unspecified });

        Assert.Single(points);
    }

    // ---------- admin ----------

    [Fact]
    public async Task Admin_Delete_RemovesReportAndItsVotes()
    {
        var report = await CreateAsync(MarikinaLat, MarikinaLng);
        await VoteAsync(report.Id, VoteType.Confirm, "voter-1");

        await using var db = _postgis.CreateDbContext();
        var deleted = await new AdminReportsService(db).DeleteAsync(report.Id);

        Assert.True(deleted);
        await using var verify = _postgis.CreateDbContext();
        Assert.Empty(verify.FloodReports);
        Assert.Empty(verify.ReportVotes);
        Assert.False(await new AdminReportsService(verify).DeleteAsync(report.Id));
    }

    [Fact]
    public async Task Admin_SetStatus_AndPagedListing()
    {
        var first = await CreateAsync(MarikinaLat, MarikinaLng);
        await CreateAsync(MarikinaLat + 0.01, MarikinaLng);
        await CreateAsync(MarikinaLat + 0.02, MarikinaLng);

        await using var db = _postgis.CreateDbContext();
        var service = new AdminReportsService(db);
        var result = await service.SetStatusAsync(first.Id, ReportStatus.Resolved);
        Assert.Equal(ReportStatus.Resolved, result.Status);

        var page = await service.GetReportsAsync(new AdminGetReportsQuery { Page = 1, PageSize = 2 });
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Reports.Count);

        var resolvedOnly = await service.GetReportsAsync(new AdminGetReportsQuery { Status = ReportStatus.Resolved });
        Assert.Equal(first.Id, Assert.Single(resolvedOnly.Reports).Id);

        await Assert.ThrowsAsync<NotFoundException>(() => service.SetStatusAsync(Guid.NewGuid(), ReportStatus.Resolved));
    }

    // ---------- trust score ----------

    [Fact]
    public async Task Trust_SummarisesASessionsReports()
    {
        var a = await CreateAsync(MarikinaLat, MarikinaLng, session: "trusted");
        await CreateAsync(MarikinaLat + 0.01, MarikinaLng, session: "trusted");
        await CreateAsync(CebuLat, CebuLng, session: "someone-else");
        await using (var db = _postgis.CreateDbContext())
        {
            await new AdminReportsService(db).SetStatusAsync(a.Id, ReportStatus.Resolved);
        }

        await using var query = _postgis.CreateDbContext();
        var trust = await new SessionTrustService(query).GetTrustScoreAsync("trusted");

        Assert.Equal(2, trust.TotalReports);
        Assert.Equal(1, trust.ResolvedReports);
        Assert.Equal(1, trust.ActiveReports);
        Assert.Equal(50, trust.TrustScore);
        Assert.Equal(DateTimeKind.Utc, trust.FirstReportAt!.Value.Kind);

        var unknown = await new SessionTrustService(query).GetTrustScoreAsync("never-reported");
        Assert.Null(unknown.TrustScore);
    }

    // ---------- push subscriptions ----------

    [Fact]
    public async Task Push_SubscribeTwiceWithSameEndpoint_UpdatesInsteadOfDuplicating()
    {
        PushSubscriptionDto Dto(string session, string key) => new()
        {
            SessionId = session,
            Endpoint = "https://push.example.com/abc",
            Keys = new PushSubscriptionKeysDto { P256dh = key, Auth = "auth" }
        };

        await using var db = _postgis.CreateDbContext();
        var service = new PushSubscriptionService(db);
        await service.SubscribeAsync(Dto("session-1", "key-1"));
        await service.SubscribeAsync(Dto("session-2", "key-2"));

        var all = await service.GetAllAsync();
        var sub = Assert.Single(all);
        Assert.Equal("session-2", sub.SessionId);
        Assert.Equal("key-2", sub.P256dh);

        await service.UnsubscribeAsync("https://push.example.com/abc");
        Assert.Empty(await service.GetAllAsync());
    }

    // ---------- demo data ----------

    [Fact]
    public async Task DemoSeeder_IsRepeatableAndOnlyReplacesItsOwnRows()
    {
        await CreateAsync(MarikinaLat, MarikinaLng, session: "real-user");

        await using var db = _postgis.CreateDbContext();
        await DemoDataSeeder.SeedAsync(db, TimeSpan.FromHours(6));
        var firstCount = await db.FloodReports.CountAsync(r => r.ReporterSessionId == DemoDataSeeder.DemoSessionId);
        await DemoDataSeeder.SeedAsync(db, TimeSpan.FromHours(6));

        Assert.True(firstCount > 0);
        Assert.Equal(firstCount, await db.FloodReports.CountAsync(r => r.ReporterSessionId == DemoDataSeeder.DemoSessionId));
        Assert.Equal(1, await db.FloodReports.CountAsync(r => r.ReporterSessionId == "real-user"));
        Assert.True(await db.FloodReports.AnyAsync(r => r.Status == ReportStatus.Active && r.ExpiresAt > DateTime.UtcNow));
    }
}
