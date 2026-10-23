using System.Security.Cryptography;
using System.Text;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Security;

// Who is this anonymous visitor? Reporters and voters have no accounts, so the
// server issues each browser a random id in a signed, HttpOnly cookie. Unlike
// the old client-generated id (kept in localStorage and trivially reset or
// forged in a request body), a client can't choose or tamper with it.
public interface ISessionIdentity
{
    // Returns the visitor's session id, creating and issuing a new cookie if
    // there is none yet or the signature doesn't verify.
    string GetOrCreate(HttpContext context);
}

public class SessionIdentity : ISessionIdentity
{
    private readonly SecurityOptions _options;
    private readonly byte[] _key;

    public SessionIdentity(IOptions<SecurityOptions> options, ISigningKey signingKey)
    {
        _options = options.Value;
        // The raw key, unchanged from before, so visitors keep their existing sessions
        _key = signingKey.Bytes;
    }

    public string GetOrCreate(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(_options.SessionCookieName, out var cookie) &&
            TryValidate(cookie, out var existing))
        {
            return existing;
        }

        var id = Base64Url(RandomNumberGenerator.GetBytes(18));
        context.Response.Cookies.Append(_options.SessionCookieName, $"{id}.{Sign(id)}", new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = Enum.Parse<SameSiteMode>(_options.SessionCookieSameSite),
            MaxAge = TimeSpan.FromDays(_options.SessionCookieDays),
            IsEssential = true
        });

        return id;
    }

    private bool TryValidate(string? cookie, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(cookie)) return false;

        var parts = cookie.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0) return false;

        // Constant-time comparison so the signature can't be guessed byte by byte
        var expected = Encoding.ASCII.GetBytes(Sign(parts[0]));
        var actual = Encoding.ASCII.GetBytes(parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;

        id = parts[0];
        return true;
    }

    private string Sign(string id) => Base64Url(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(id)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
