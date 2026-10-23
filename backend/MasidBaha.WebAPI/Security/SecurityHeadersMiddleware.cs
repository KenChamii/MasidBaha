namespace MasidBaha.WebAPI.Security;

// Baseline hardening headers for every response. This is a JSON API plus
// static photos, so the policy is strict: nothing from here is meant to be
// framed, sniffed or to load active content.
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

            // The docs page needs scripts and styles, everything else gets a locked-down policy
            if (!context.Request.Path.StartsWithSegments("/docs") && !context.Request.Path.StartsWithSegments("/swagger"))
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
