namespace MasidBaha.WebAPI.Middleware;

// Deliberately simple: a single shared API key checked via header, not a
// full identity/JWT system. 
public class AdminAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-Admin-Key";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    public AdminAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<AdminAuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only guards /api/admin/** — everything else passes through untouched.
        if (!context.Request.Path.StartsWithSegments("/api/admin"))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            // Fail closed: if no key is configured, admin routes are
            // disabled entirely rather than silently left open.
            _logger.LogWarning("Admin:ApiKey is not configured — blocking request to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Admin panel is not configured." });
            return;
        }

        var providedKey = context.Request.Headers[ApiKeyHeaderName].ToString();
        if (string.IsNullOrEmpty(providedKey) || providedKey != configuredKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing admin API key." });
            return;
        }

        await _next(context);
    }
}
