using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MasidBaha.Application.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MasidBaha.Tests.Integration;

// The back-office features through the real HTTP pipeline: users, bans, moderation,
// settings, audit, export, 2FA and role checks
[Collection(PostgisCollection.Name)]
public class AdminPortalTests : IAsyncLifetime
{
    private const string Api = "/api/v1";
    private const string Admin = "/api/v1/admin";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public AdminPortalTests(PostgisFixture postgis) => _postgis = postgis;

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

    private static object NewReport(double lat = 14.65, string notes = "Water is rising") =>
        new { lat, lng = 121.10, severity = 2, notes };

    private async Task<Guid> PostReportAsync(HttpClient client, double lat = 14.65, string notes = "Water is rising")
    {
        var response = await client.PostAsJsonAsync($"{Api}/flood-reports", NewReport(lat, notes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    // ---------- roles ----------

    [Fact]
    public async Task Roles_DecideWhoCanReadActAndManage()
    {
        var viewer = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Admin}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Admin}/moderation/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{Admin}/broadcasts", new { title = "t", body = "b" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = new[] { Guid.NewGuid() }, action = 1 })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await moderator.PostAsJsonAsync($"{Admin}/broadcasts", new { title = "Typhoon", body = "Stay safe" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync($"{Admin}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync($"{Admin}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync($"{Admin}/users")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"{Admin}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"{Admin}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"{Admin}/users")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"{Admin}/dashboard")).StatusCode);
    }

    // ---------- users ----------

    [Fact]
    public async Task NewStaff_MustReplaceTheTemporaryPassword_BeforeDoingAnything()
    {
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);
        var email = $"new-{Guid.NewGuid():N}@test.local";

        var created = await superAdmin.PostAsJsonAsync($"{Admin}/users", new { email, displayName = "New Person", role = AdminRoles.Moderator });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var temp = (await JsonAsync(created)).GetProperty("temporaryPassword").GetString()!;

        var staff = _factory.CreateClient();
        var login = await staff.PostAsJsonAsync($"{Api}/auth/login", new { email, password = temp });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await JsonAsync(login)).GetProperty("mustChangePassword").GetBoolean());

        // Signed in, but every admin endpoint refuses until the password is replaced
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"{Admin}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Api}/auth/me")).StatusCode);

        var changed = await staff.PostAsJsonAsync($"{Api}/auth/change-password", new { currentPassword = temp, newPassword = "a-brand-new-password-123" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await JsonAsync(changed)).GetProperty("mustChangePassword").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Admin}/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Users_CannotRemoveTheLastSuperAdmin_OrThemselves()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.SuperAdmin);
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword })).EnsureSuccessStatusCode();
        var me = (await JsonAsync(await client.GetAsync($"{Api}/auth/me"))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"{Admin}/users/{me}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{Admin}/users/{me}", new { role = AdminRoles.Viewer })).StatusCode);
    }

    [Fact]
    public async Task DeactivatedStaff_CannotSignIn_AndAreSignedOut()
    {
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);
        var email = await _factory.CreateStaffAsync(AdminRoles.Moderator);
        var staff = _factory.CreateClient();
        (await staff.PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword })).EnsureSuccessStatusCode();

        var list = await JsonAsync(await superAdmin.GetAsync($"{Admin}/users?search={Uri.EscapeDataString(email)}"));
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsync($"{Admin}/users/{id}/deactivate", null)).StatusCode);

        var again = await _factory.CreateClient().PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    // ---------- bans ----------

    [Fact]
    public async Task BlockBan_RejectsTheReporterFromPostingAgain()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var reporter = _factory.CreateClient();
        var reportId = await PostReportAsync(reporter);

        var ban = await moderator.PostAsJsonAsync($"{Admin}/bans",
            new { reportId, kind = 1, mode = 1, reason = "Spam pins", hideExistingReports = true });
        Assert.Equal(HttpStatusCode.Created, ban.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reporter.PostAsJsonAsync($"{Api}/flood-reports", NewReport())).StatusCode);

        // Their earlier report is gone from the public map
        var nearby = await _factory.CreateClient().GetFromJsonAsync<JsonElement>($"{Api}/flood-reports?lat=14.65&lng=121.10&radiusMeters=5000");
        Assert.Equal(0, nearby.GetArrayLength());

        // Banning the same source twice is a conflict; lifting lets them back in
        Assert.Equal(HttpStatusCode.Conflict, (await moderator.PostAsJsonAsync($"{Admin}/bans", new { reportId, kind = 1, mode = 1, reason = "again" })).StatusCode);
        var banId = (await JsonAsync(ban)).GetProperty("ban").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await moderator.DeleteAsync($"{Admin}/bans/{banId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await reporter.PostAsJsonAsync($"{Api}/flood-reports", NewReport())).StatusCode);
    }

    [Fact]
    public async Task ShadowBan_LooksLikeSuccess_ButNothingIsPublic()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var reporter = _factory.CreateClient();
        var first = await PostReportAsync(reporter);

        (await moderator.PostAsJsonAsync($"{Admin}/bans", new { reportId = first, kind = 1, mode = 2, reason = "Suspicious" })).EnsureSuccessStatusCode();

        // Still answers 201, so the reporter notices nothing
        var second = await PostReportAsync(reporter, lat: 14.70);

        var nearby = await _factory.CreateClient().GetFromJsonAsync<JsonElement>($"{Api}/flood-reports?lat=14.70&lng=121.10&radiusMeters=2000");
        Assert.DoesNotContain(nearby.EnumerateArray(), r => r.GetProperty("id").GetGuid() == second);

        // Staff still see it, marked hidden
        var detail = await JsonAsync(await moderator.GetAsync($"{Admin}/reports/{second}"));
        Assert.True(detail.GetProperty("report").GetProperty("isHidden").GetBoolean());
    }

    // A report that is not public must not be reachable by its id either (share links, guessing)
    [Fact]
    public async Task HiddenReport_Is404ForEveryoneButItsReporter()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var reporter = _factory.CreateClient();
        var id = await PostReportAsync(reporter);

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync($"{Api}/flood-reports/{id}")).StatusCode);

        await moderator.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = new[] { id }, action = 5 });

        // A stranger, and someone with no cookie at all, get nothing
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"{Api}/flood-reports/{id}")).StatusCode);
        // The reporter still sees their own report, so a shadow ban gives nothing away
        Assert.Equal(HttpStatusCode.OK, (await reporter.GetAsync($"{Api}/flood-reports/{id}")).StatusCode);
    }

    [Fact]
    public async Task ReadingAReport_DoesNotIssueASessionCookie()
    {
        var id = await PostReportAsync(_factory.CreateClient());

        var response = await _factory.CreateClient().GetAsync($"{Api}/flood-reports/{id}");

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    // ---------- moderation ----------

    [Fact]
    public async Task Bulk_HideThenUnhide_ChangesWhatThePublicSees()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var id = await PostReportAsync(_factory.CreateClient());

        var hide = await moderator.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = new[] { id }, action = 5 });
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        Assert.Single((await JsonAsync(hide)).GetProperty("affectedIds").EnumerateArray());

        var hidden = await _factory.CreateClient().GetFromJsonAsync<JsonElement>($"{Api}/flood-reports?lat=14.65&lng=121.10&radiusMeters=5000");
        Assert.Equal(0, hidden.GetArrayLength());

        await moderator.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = new[] { id }, action = 6 });
        var shown = await _factory.CreateClient().GetFromJsonAsync<JsonElement>($"{Api}/flood-reports?lat=14.65&lng=121.10&radiusMeters=5000");
        Assert.Equal(1, shown.GetArrayLength());
    }

    [Fact]
    public async Task Bulk_RejectsEmptyAndOversizedRequests()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);

        Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = Array.Empty<Guid>(), action = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await moderator.PostAsJsonAsync($"{Admin}/moderation/bulk", new { ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()), action = 1 })).StatusCode);
    }

    // ---------- reports: search, sort, export, detail, map ----------

    [Fact]
    public async Task Reports_SearchSortAndMapShareTheFilters()
    {
        var viewer = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);
        var reporter = _factory.CreateClient();
        await PostReportAsync(reporter, 14.65, "Knee deep near the bridge");
        await PostReportAsync(reporter, 14.66, "Road is dry");

        var found = await JsonAsync(await viewer.GetAsync($"{Admin}/reports?search=bridge"));
        Assert.Equal(1, found.GetProperty("totalCount").GetInt32());

        var all = await JsonAsync(await viewer.GetAsync($"{Admin}/reports?sortBy=confidence&descending=false"));
        Assert.Equal(2, all.GetProperty("reports").GetArrayLength());

        var map = await JsonAsync(await viewer.GetAsync($"{Admin}/reports/map?search=bridge"));
        Assert.Equal(1, map.GetArrayLength());

        // A search containing LIKE wildcards matches literally, not "everything"
        var wildcard = await JsonAsync(await viewer.GetAsync($"{Admin}/reports?search=%25"));
        Assert.Equal(0, wildcard.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Export_IsCsv_DefusesFormulas_AndIsAudited()
    {
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);
        await PostReportAsync(_factory.CreateClient(), notes: "=HYPERLINK(\"http://evil\")");

        var response = await superAdmin.GetAsync($"{Admin}/reports/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());

        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"'=HYPERLINK", csv);

        var audit = await JsonAsync(await superAdmin.GetAsync($"{Admin}/audit?action=report.exported"));
        Assert.Equal(1, audit.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Detail_ShowsVotesTrustAndHistory_And404sForUnknownReports()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var id = await PostReportAsync(_factory.CreateClient());
        await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports/{id}/vote", new { voteType = 1 });
        await moderator.PatchAsJsonAsync($"{Admin}/reports/{id}/status", new { status = 2 });

        var detail = await JsonAsync(await moderator.GetAsync($"{Admin}/reports/{id}"));
        Assert.Equal(1, detail.GetProperty("votes").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("reporterTrust").GetProperty("totalReports").GetInt32());
        Assert.True(detail.GetProperty("history").GetArrayLength() >= 1);

        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync($"{Admin}/reports/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- settings, audit, dashboard, broadcast ----------

    [Fact]
    public async Task Settings_OverrideTakesEffectAtOnce_AndResetRestoresDefaults()
    {
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);

        var view = await JsonAsync(await superAdmin.GetAsync($"{Admin}/settings"));
        var current = view.GetProperty("current");
        var update = new
        {
            reportLifetimeHours = 12,
            pushMinimumSeverity = current.GetProperty("pushMinimumSeverity").GetInt32(),
            denyFlagThreshold = current.GetProperty("denyFlagThreshold").GetInt32(),
            floodingReportsPerHour = current.GetProperty("floodingReportsPerHour").GetInt32(),
            reportWritesPerMinute = current.GetProperty("reportWritesPerMinute").GetInt32(),
            voteWritesPerMinute = current.GetProperty("voteWritesPerMinute").GetInt32()
        };

        var put = await superAdmin.PutAsJsonAsync($"{Admin}/settings", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Contains("reports.lifetimeHours", (await JsonAsync(put)).GetProperty("overridden").EnumerateArray().Select(e => e.GetString()));

        var id = await PostReportAsync(_factory.CreateClient());
        var created = await JsonAsync(await superAdmin.GetAsync($"{Admin}/reports/{id}"));
        var report = created.GetProperty("report");
        var lifetime = report.GetProperty("expiresAt").GetDateTime() - report.GetProperty("reportedAt").GetDateTime();
        Assert.InRange(lifetime.TotalHours, 11.9, 12.1);

        var reset = await JsonAsync(await superAdmin.DeleteAsync($"{Admin}/settings"));
        Assert.Empty(reset.GetProperty("overridden").EnumerateArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PutAsJsonAsync($"{Admin}/settings", new { reportLifetimeHours = 500 })).StatusCode);
    }

    [Fact]
    public async Task Audit_RecordsWhoDidWhat()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);
        var id = await PostReportAsync(_factory.CreateClient());

        await moderator.PatchAsJsonAsync($"{Admin}/reports/{id}/status", new { status = 2 });

        var entries = await JsonAsync(await superAdmin.GetAsync($"{Admin}/audit?targetType=report&targetId={id}"));
        var entry = Assert.Single(entries.GetProperty("items").EnumerateArray());
        Assert.Equal("report.status_changed", entry.GetProperty("action").GetString());
        Assert.StartsWith("moderator-", entry.GetProperty("actorEmail").GetString());

        // Failed sign-ins are recorded too, without revealing whether the account exists
        await _factory.CreateClient().PostAsJsonAsync($"{Api}/auth/login", new { email = "nobody@test.local", password = "whatever-password" });
        var failed = await JsonAsync(await superAdmin.GetAsync($"{Admin}/audit?action=auth.login_failed"));
        Assert.Equal(1, failed.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Dashboard_CountsReports_AndReturnsAContinuousSeries()
    {
        var viewer = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);
        await PostReportAsync(_factory.CreateClient());

        var dashboard = await JsonAsync(await viewer.GetAsync($"{Admin}/dashboard"));
        Assert.Equal(1, dashboard.GetProperty("activeReports").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("reportsLast24h").GetInt32());
        Assert.Equal(31, dashboard.GetProperty("dailyReports30d").GetArrayLength());
        Assert.Equal(4, dashboard.GetProperty("severityBreakdown30d").GetArrayLength());
    }

    [Fact]
    public async Task Broadcast_IsQueuedForPush_RecordedAndRejectsExternalLinks()
    {
        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);

        var sent = await moderator.PostAsJsonAsync($"{Admin}/broadcasts", new { title = "Typhoon warning", body = "Move to higher ground", url = "/map" });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Contains(_factory.Push.Enqueued, p => p.Title == "Typhoon warning");

        var list = await JsonAsync(await moderator.GetAsync($"{Admin}/broadcasts"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await moderator.PostAsJsonAsync($"{Admin}/broadcasts", new { title = "x", body = "y", url = "https://evil.example.net" })).StatusCode);
    }

    // ---------- two-factor ----------

    [Fact]
    public async Task TwoFactor_SetupEnableAndSignInWithACode()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.Moderator);
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword })).EnsureSuccessStatusCode();

        var setup = await JsonAsync(await client.PostAsync($"{Api}/auth/2fa/setup", null));
        var key = setup.GetProperty("sharedKey").GetString()!.Replace(" ", "");

        // A wrong code does not turn it on
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Api}/auth/2fa/enable", new { code = "000000" })).StatusCode);

        var enabled = await client.PostAsJsonAsync($"{Api}/auth/2fa/enable", new { code = Totp(key) });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.Equal(10, (await JsonAsync(enabled)).GetProperty("recoveryCodes").GetArrayLength());

        // Next sign-in: the password alone is no longer enough
        var browser = _factory.CreateClient();
        var step1 = await browser.PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword });
        Assert.Equal(HttpStatusCode.Accepted, step1.StatusCode);
        Assert.True((await JsonAsync(step1)).GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync($"{Api}/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync($"{Api}/auth/login/2fa", new { code = "000000" })).StatusCode);

        var step2 = await browser.PostAsJsonAsync($"{Api}/auth/login/2fa", new { code = Totp(key) });
        Assert.Equal(HttpStatusCode.OK, step2.StatusCode);
        Assert.True((await JsonAsync(step2)).GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task TwoFactor_SuperAdminCanResetItForALockedOutColleague()
    {
        var superAdmin = await _factory.CreateSignedInClientAsync(AdminRoles.SuperAdmin);
        var email = await _factory.CreateStaffAsync(AdminRoles.Viewer);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.SetTwoFactorEnabledAsync(user, true);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await _factory.CreateClient().PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword })).StatusCode);

        var list = await JsonAsync(await superAdmin.GetAsync($"{Admin}/users?search={Uri.EscapeDataString(email)}"));
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsync($"{Admin}/users/{id}/reset-2fa", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().PostAsJsonAsync($"{Api}/auth/login", new { email, password = ApiFactory.StaffPassword })).StatusCode);
    }

    // RFC 6238 code for the current 30-second step (what an authenticator app shows)
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32Key.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);

        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
