using MasidBaha.Application.Common.Options;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MasidBaha.Tests;

public class SessionIdentityTests
{
    private const string CookieName = "mb_session";

    private class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static SessionIdentity Create(string? key = "k-this-is-a-test-signing-key-32-chars!", string environment = "Development") =>
        new(Options.Create(new SecurityOptions { SessionSigningKey = key }),
            new FakeEnvironment { EnvironmentName = environment },
            NullLogger<SessionIdentity>.Instance);

    // Reads the cookie value the server put in Set-Cookie
    private static string? IssuedCookie(HttpContext context)
    {
        var setCookie = context.Response.Headers[HeaderNames.SetCookie].ToString();
        if (string.IsNullOrEmpty(setCookie)) return null;
        var pair = setCookie.Split(';')[0];
        return pair[(pair.IndexOf('=') + 1)..];
    }

    private static DefaultHttpContext RequestWithCookie(string? value)
    {
        var context = new DefaultHttpContext();
        if (value is not null) context.Request.Headers.Cookie = $"{CookieName}={value}";
        return context;
    }

    [Fact]
    public void FirstVisit_GetsAFreshIdAndASignedHttpOnlyCookie()
    {
        var context = RequestWithCookie(null);

        var id = Create().GetOrCreate(context);

        Assert.False(string.IsNullOrWhiteSpace(id));
        var header = context.Response.Headers[HeaderNames.SetCookie].ToString();
        Assert.StartsWith($"{CookieName}={id}.", header);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturningVisitor_KeepsTheSameIdAndGetsNoNewCookie()
    {
        var sessions = Create();
        var first = RequestWithCookie(null);
        var id = sessions.GetOrCreate(first);

        var second = RequestWithCookie(IssuedCookie(first));
        var again = sessions.GetOrCreate(second);

        Assert.Equal(id, again);
        Assert.True(string.IsNullOrEmpty(second.Response.Headers[HeaderNames.SetCookie].ToString()));
    }

    [Fact]
    public void TamperedId_IsReplacedNotTrusted()
    {
        var sessions = Create();
        var first = RequestWithCookie(null);
        var id = sessions.GetOrCreate(first);
        var signature = IssuedCookie(first)![(id.Length + 1)..];

        // Someone keeps the signature but swaps in another id (e.g. to impersonate a session)
        var forged = RequestWithCookie($"someone-elses-id.{signature}");
        var result = sessions.GetOrCreate(forged);

        Assert.NotEqual("someone-elses-id", result);
        Assert.False(string.IsNullOrEmpty(forged.Response.Headers[HeaderNames.SetCookie].ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-dot-at-all")]
    [InlineData(".")]
    [InlineData("a.b.c")]
    [InlineData("id.not-a-real-signature")]
    public void MalformedCookies_AreIgnored(string cookie)
    {
        var context = RequestWithCookie(cookie);

        var id = Create().GetOrCreate(context);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.False(string.IsNullOrEmpty(context.Response.Headers[HeaderNames.SetCookie].ToString()));
    }

    [Fact]
    public void ACookieSignedWithAnotherKey_IsRejected()
    {
        var other = RequestWithCookie(null);
        var otherId = Create("a-completely-different-signing-key-xx").GetOrCreate(other);

        var context = RequestWithCookie(IssuedCookie(other));
        var result = Create().GetOrCreate(context);

        Assert.NotEqual(otherId, result);
    }

    [Fact]
    public void EachNewVisitorGetsADifferentId()
    {
        var sessions = Create();

        var ids = Enumerable.Range(0, 50).Select(_ => sessions.GetOrCreate(RequestWithCookie(null))).ToList();

        Assert.Equal(50, ids.Distinct().Count());
    }

    [Fact]
    public void MissingKey_IsAllowedInDevelopmentButFailsInProduction()
    {
        Assert.NotNull(Create(key: null, environment: "Development"));

        var ex = Assert.Throws<InvalidOperationException>(() => Create(key: null, environment: "Production"));
        Assert.Contains("SessionSigningKey", ex.Message);
    }
}
