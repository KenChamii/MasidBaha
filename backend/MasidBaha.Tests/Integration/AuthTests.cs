using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasidBaha.Tests.Integration;

// Back-office authentication and authorization through the real HTTP pipeline
[Collection(PostgisCollection.Name)]
public class AuthTests : IAsyncLifetime
{
    private const string Api = "/api/v1";

    private readonly PostgisFixture _postgis;
    private ApiFactory _factory = null!;

    public AuthTests(PostgisFixture postgis) => _postgis = postgis;

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

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync($"{Api}/auth/login", new { email, password });

    private async Task<Guid> CreateReportAsync()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync($"{Api}/flood-reports", new { lat = 14.65, lng = 121.10, severity = 2 });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_SetsAnHttpOnlyCookie_AndReturnsTheUserWithRoles()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.Moderator);
        var client = _factory.CreateClient();

        var response = await LoginAsync(client, email, ApiFactory.StaffPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("mb_admin=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var user = await JsonAsync(response);
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal(AdminRoles.Moderator, user.GetProperty("roles")[0].GetString());
        Assert.False(user.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Me_ReturnsTheSignedInUser_AndIs401WithoutALogin()
    {
        var signedIn = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);

        var me = await signedIn.GetAsync($"{Api}/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(AdminRoles.Viewer, (await JsonAsync(me)).GetProperty("roles")[0].GetString());

        var anonymous = await _factory.CreateClient().GetAsync($"{Api}/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmail_AreIndistinguishable()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.Viewer);
        var client = _factory.CreateClient();

        var wrongPassword = await LoginAsync(client, email, "definitely-not-the-password");
        var unknownEmail = await LoginAsync(client, "nobody@test.local", ApiFactory.StaffPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            (await JsonAsync(wrongPassword)).GetProperty("detail").GetString(),
            (await JsonAsync(unknownEmail)).GetProperty("detail").GetString());
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task RepeatedFailures_LockTheAccount_EvenForTheCorrectPassword()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.Moderator);
        var client = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, "wrong-password-123")).StatusCode);

        var correct = await LoginAsync(client, email, ApiFactory.StaffPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Contains("try again later", (await JsonAsync(correct)).GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(correct.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("not-an-email", "whatever")]
    [InlineData("", "whatever")]
    [InlineData("someone@test.local", "")]
    public async Task Login_ValidatesTheRequest(string email, string password)
    {
        var response = await LoginAsync(_factory.CreateClient(), email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Login_AcceptsJsonOnly()
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "a@test.local", ["password"] = "x" });

        var response = await _factory.CreateClient().PostAsync($"{Api}/auth/login", form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimitedPerClient()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["RateLimits:AuthAttempts:PermitLimit"] = "3" });
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            await LoginAsync(client, "nobody@test.local", "whatever-password");

        var limited = await LoginAsync(client, "nobody@test.local", "whatever-password");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    // ---------- logout and session ----------

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Api}/admin/reports")).StatusCode);

        var logout = await client.PostAsync($"{Api}/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Api}/admin/reports")).StatusCode);
    }

    [Fact]
    public async Task Logout_WhenNotSignedIn_IsHarmless()
    {
        var response = await _factory.CreateClient().PostAsync($"{Api}/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task APublicSessionCookie_DoesNotGrantAdminAccess()
    {
        var visitor = _factory.CreateClient();
        await visitor.PostAsJsonAsync($"{Api}/flood-reports", new { lat = 14.65, lng = 121.10, severity = 2 }); // gets the visitor cookie

        var response = await visitor.GetAsync($"{Api}/admin/reports");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- roles ----------

    [Fact]
    public async Task Anonymous_CannotUseAnyAdminEndpoint()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Api}/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Api}/admin/sessions/x/trust")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PatchAsync($"{Api}/admin/reports/{id}/status", JsonContent.Create(new { status = 2 }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"{Api}/admin/reports/{id}")).StatusCode);
    }

    [Fact]
    public async Task Viewer_CanRead_ButCannotModerate()
    {
        var reportId = await CreateReportAsync();
        var viewer = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Api}/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Api}/admin/sessions/anything/trust")).StatusCode);

        var patch = await viewer.PatchAsync($"{Api}/admin/reports/{reportId}/status", JsonContent.Create(new { status = 2 }));
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal("application/problem+json", patch.Content.Headers.ContentType!.MediaType);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Api}/admin/reports/{reportId}")).StatusCode);

        // and nothing changed
        await using var db = _postgis.CreateDbContext();
        Assert.Equal(ReportStatus.Active, (await db.FloodReports.FindAsync(reportId))!.Status);
    }

    [Theory]
    [InlineData(AdminRoles.Moderator)]
    [InlineData(AdminRoles.SuperAdmin)]
    public async Task ModeratorsAndSuperAdmins_CanModerate(string role)
    {
        var reportId = await CreateReportAsync();
        var staff = await _factory.CreateSignedInClientAsync(role);

        var resolve = await staff.PatchAsync($"{Api}/admin/reports/{reportId}/status", JsonContent.Create(new { status = (int)ReportStatus.Resolved }));
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);

        var bad = await staff.PatchAsync($"{Api}/admin/reports/{reportId}/status", JsonContent.Create(new { status = 9 }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"{Api}/admin/reports/{reportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.DeleteAsync($"{Api}/admin/reports/{reportId}")).StatusCode);
    }

    [Fact]
    public async Task Moderation_RefreshesThePublicTopListAtOnce()
    {
        var reportId = await CreateReportAsync();
        var visitor = _factory.CreateClient();
        Assert.Equal(1, (await visitor.GetFromJsonAsync<JsonElement>($"{Api}/flood-reports/top")).GetArrayLength()); // cached now

        var moderator = await _factory.CreateSignedInClientAsync(AdminRoles.Moderator);
        await moderator.PatchAsync($"{Api}/admin/reports/{reportId}/status", JsonContent.Create(new { status = (int)ReportStatus.Resolved }));

        Assert.Equal(0, (await visitor.GetFromJsonAsync<JsonElement>($"{Api}/flood-reports/top")).GetArrayLength());
    }

    // ---------- passwords ----------

    [Fact]
    public async Task ChangePassword_RequiresTheCurrentOne_AndTheNewOneWorksAfterwards()
    {
        var email = await _factory.CreateStaffAsync(AdminRoles.Moderator);
        var client = _factory.CreateClient();
        await LoginAsync(client, email, ApiFactory.StaffPassword);
        const string newPassword = "a-brand-new-long-passphrase";

        var wrongCurrent = await client.PostAsJsonAsync($"{Api}/auth/change-password",
            new { currentPassword = "not-my-password", newPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.True((await JsonAsync(wrongCurrent)).GetProperty("errors").TryGetProperty("CurrentPassword", out _));

        var changed = await client.PostAsJsonAsync($"{Api}/auth/change-password",
            new { currentPassword = ApiFactory.StaffPassword, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // this session survives, the old password doesn't, the new one does
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Api}/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_factory.CreateClient(), email, ApiFactory.StaffPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_factory.CreateClient(), email, newPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RejectsAShortPassword_WithAReason()
    {
        var client = await _factory.CreateSignedInClientAsync(AdminRoles.Viewer);

        var response = await client.PostAsJsonAsync($"{Api}/auth/change-password",
            new { currentPassword = ApiFactory.StaffPassword, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("NewPassword", out _));
    }

    [Fact]
    public async Task ChangePassword_NeedsASignedInUser()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync($"{Api}/auth/change-password",
            new { currentPassword = "x", newPassword = "a-brand-new-long-passphrase" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangingAPassword_SignsOutTheOtherSessions_ButNotTheOneThatChangedIt()
    {
        // Re-check every request, so the effect is immediate instead of within a minute
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["AdminAuth:SecurityCheckSeconds"] = "0" });
        var email = await factory.CreateStaffAsync(AdminRoles.Moderator);
        var laptop = factory.CreateClient();
        var phone = factory.CreateClient();
        await LoginAsync(laptop, email, ApiFactory.StaffPassword);
        await LoginAsync(phone, email, ApiFactory.StaffPassword);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync($"{Api}/auth/me")).StatusCode);

        await laptop.PostAsJsonAsync($"{Api}/auth/change-password",
            new { currentPassword = ApiFactory.StaffPassword, newPassword = "a-brand-new-long-passphrase" });

        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync($"{Api}/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync($"{Api}/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ARoleChange_TakesEffectOnAnExistingSession()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["AdminAuth:SecurityCheckSeconds"] = "0" });
        var email = await factory.CreateStaffAsync(AdminRoles.Moderator);
        var client = factory.CreateClient();
        await LoginAsync(client, email, ApiFactory.StaffPassword);
        // A moderator gets past authorization (an invalid status then fails validation with 400)
        var before = await client.PatchAsync($"{Api}/admin/reports/{Guid.NewGuid()}/status", JsonContent.Create(new { status = 9 }));
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.RemoveFromRoleAsync(user, AdminRoles.Moderator);
            await users.AddToRoleAsync(user, AdminRoles.Viewer);
        }

        var demoted = await client.PatchAsync($"{Api}/admin/reports/{Guid.NewGuid()}/status", JsonContent.Create(new { status = 2 }));
        Assert.Equal(HttpStatusCode.Forbidden, demoted.StatusCode);
    }

    // ---------- seeding ----------

    [Fact]
    public async Task Startup_SeedsTheRoles()
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in AdminRoles.All)
            Assert.True(await roles.RoleExistsAsync(role), $"role {role} should exist");
    }

    [Fact]
    public async Task InitialAdmin_IsCreatedOnlyWhenConfigured_AndNeverTwice()
    {
        var email = $"first-{Guid.NewGuid():N}@test.local";
        var settings = new Dictionary<string, string?>
        {
            ["AdminAuth:InitialAdmin:Email"] = email,
            ["AdminAuth:InitialAdmin:Password"] = "initial-admin-passphrase-1"
        };

        // Remove any SuperAdmin left by other tests so the bootstrap condition is real
        await using (var db = _postgis.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM admin_user_roles");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM admin_users");
        }

        using var first = new ApiFactory(_postgis.ConnectionString, settings);
        var client = first.CreateClient();

        var login = await LoginAsync(client, email, "initial-admin-passphrase-1");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(AdminRoles.SuperAdmin, (await JsonAsync(login)).GetProperty("roles")[0].GetString());

        // a second start with different credentials must not add or change anything
        var other = $"second-{Guid.NewGuid():N}@test.local";
        using var second = new ApiFactory(_postgis.ConnectionString, new()
        {
            ["AdminAuth:InitialAdmin:Email"] = other,
            ["AdminAuth:InitialAdmin:Password"] = "another-passphrase-12345"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(second.CreateClient(), other, "another-passphrase-12345")).StatusCode);

        await using var verify = _postgis.CreateDbContext();
        Assert.Equal(1, await verify.Users.CountAsync());
    }

    [Fact]
    public async Task InitialAdmin_WithAWeakPassword_IsNotCreated()
    {
        await using (var db = _postgis.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM admin_user_roles");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM admin_users");
        }

        using var factory = new ApiFactory(_postgis.ConnectionString, new()
        {
            ["AdminAuth:InitialAdmin:Email"] = "weak@test.local",
            ["AdminAuth:InitialAdmin:Password"] = "short"
        });
        factory.CreateClient(); // boots the app, which attempts the seed

        await using var verify = _postgis.CreateDbContext();
        Assert.Equal(0, await verify.Users.CountAsync());
    }

    [Fact]
    public void BadAuthOptions_FailTheBoot()
    {
        using var factory = new ApiFactory(_postgis.ConnectionString, new() { ["AdminAuth:SessionHours"] = "0" });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("SessionHours", ex.ToString());
    }
}
