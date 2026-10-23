using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MasidBaha.Application.Common.Options;

namespace MasidBaha.WebAPI.Security;

// The Cloudflare Pages proxy sits in front of this API, so the socket address is
// Cloudflare's, and X-Forwarded-For has a platform-dependent shape that can't be
// trusted to hold the visitor. Instead the proxy sends the visitor's address in
// X-Client-IP together with a shared secret; only a request carrying the right
// secret may set the client address, so nobody else can pick the IP rate limits
// and bans see.
public class TrustedProxyMiddleware
{
    public const string SecretHeader = "X-Proxy-Secret";
    public const string ClientIpHeader = "X-Client-IP";

    private readonly RequestDelegate _next;
    private readonly byte[] _secret;

    public TrustedProxyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
    {
        _next = next;
        _secret = Encoding.UTF8.GetBytes(options.Value.TrustedProxySecret ?? string.Empty);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var presented = context.Request.Headers[SecretHeader].ToString();

        // Constant-time comparison, so the secret can't be guessed byte by byte from response timing
        if (presented.Length > 0 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _secret) &&
            IPAddress.TryParse(context.Request.Headers[ClientIpHeader].ToString(), out var client))
        {
            context.Connection.RemoteIpAddress = client;
        }

        return _next(context);
    }
}
