using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MasidBaha.WebAPI.Security;

// Guards the admin controller with a single shared key sent as X-Admin-Key.
// Deliberately simple for now (Phase 4 replaces it with real accounts and roles).
// Being a filter on the controller, rather than a check on the URL path, means
// the protection can't be bypassed by changing the route or API version.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Admin-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configuredKey = http.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            // Fail closed: with no key configured the admin API is disabled, not left open
            http.RequestServices.GetRequiredService<ILogger<AdminApiKeyAttribute>>()
                .LogWarning("Admin:ApiKey is not configured; blocking request to {Path}", http.Request.Path);

            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Admin API is not configured."
            }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return Task.CompletedTask;
        }

        var provided = http.Request.Headers[HeaderName].ToString();
        if (!KeysMatch(provided, configuredKey))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid or missing admin API key."
            }) { StatusCode = StatusCodes.Status401Unauthorized };
        }

        return Task.CompletedTask;
    }

    // Constant-time compare (hashing first makes the lengths equal)
    private static bool KeysMatch(string provided, string expected) =>
        !string.IsNullOrEmpty(provided) &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
