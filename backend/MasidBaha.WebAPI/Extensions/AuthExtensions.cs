using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Extensions;

public static class AuthExtensions
{
    // Back-office login: ASP.NET Identity on the same PostgreSQL database,
    // with an HttpOnly cookie as the session. Anonymous reporters and voters
    // are not part of this; they only have the signed session cookie.
    public static IServiceCollection AddMasidBahaAuth(this IServiceCollection services)
    {
        services.AddIdentityCore<AdminUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MasidBahaDbContext>()
            .AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        // Identity rules come from AdminAuth options rather than hardcoded values
        services.AddOptions<IdentityOptions>().Configure<IOptions<AdminAuthOptions>>((identity, admin) =>
        {
            var settings = admin.Value;

            // Length is what makes a password strong, so no forced symbol/digit rules
            identity.Password.RequiredLength = settings.MinPasswordLength;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;

            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = settings.MaxFailedAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(settings.LockoutMinutes);

            identity.User.RequireUniqueEmail = true;
        });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<AdminAuthOptions>, IOptions<SecurityOptions>>((cookie, admin, security) =>
            {
                cookie.Cookie.Name = admin.Value.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS wherever the API is served over it
                cookie.Cookie.SameSite = Enum.Parse<SameSiteMode>(security.Value.SessionCookieSameSite);
                cookie.ExpireTimeSpan = TimeSpan.FromHours(admin.Value.SessionHours);
                cookie.SlidingExpiration = true;

                // An API answers with a status code, never an HTML login redirect
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // Re-check the account often, so a deactivated user or a changed role takes effect quickly
        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<AdminAuthOptions>>((options, admin) =>
            options.ValidationInterval = TimeSpan.FromSeconds(admin.Value.SecurityCheckSeconds));

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicies.CanView, policy => policy.RequireRole(AdminRoles.All))
            .AddPolicy(AdminPolicies.CanModerate, policy => policy.RequireRole(AdminRoles.Moderator, AdminRoles.SuperAdmin))
            .AddPolicy(AdminPolicies.ManageUsers, policy => policy.RequireRole(AdminRoles.SuperAdmin));

        return services;
    }
}
