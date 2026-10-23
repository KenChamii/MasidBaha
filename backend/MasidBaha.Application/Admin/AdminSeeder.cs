using MasidBaha.Application.Common.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.Admin;

// Runs at startup: makes sure the roles exist, and bootstraps the very first
// SuperAdmin from configuration so a fresh deployment isn't locked out of its
// own back-office. It never overwrites or resets an existing account.
public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder");
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = services.GetRequiredService<UserManager<AdminUser>>();
        var initial = services.GetRequiredService<IOptions<AdminAuthOptions>>().Value.InitialAdmin;

        foreach (var role in AdminRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
        }

        if (!initial.IsConfigured)
            return;

        if ((await users.GetUsersInRoleAsync(AdminRoles.SuperAdmin)).Count > 0)
            return;

        var email = initial.Email!.Trim();
        var user = new AdminUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = initial.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user, initial.Password!);
        if (!created.Succeeded)
        {
            // Most likely the password breaks the strength rules. Never log the password itself.
            logger.LogError("Could not create the initial admin {Email}: {Errors}",
                email, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(user, AdminRoles.SuperAdmin);
        logger.LogWarning("Created the initial SuperAdmin {Email}. Change this password after the first login.", email);
    }
}
