using System.Security.Cryptography;
using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Common;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin.Users;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AdminUserQuery : PagedQuery
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public bool? Active { get; set; }
}

public class CreateAdminUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = AdminRoles.Viewer;
}

public class UpdateAdminUserRequest
{
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
}

// Shown to the super admin once; only the hash is stored
public class UserCredentialDto
{
    public AdminUserDto User { get; set; } = new();
    public string TemporaryPassword { get; set; } = string.Empty;
}

public class AdminUserQueryValidator : AbstractValidator<AdminUserQuery>
{
    public AdminUserQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Role).Must(r => r is null || AdminRoles.All.Contains(r)).WithMessage("Unknown role.");
    }
}

public class CreateAdminUserRequestValidator : AbstractValidator<CreateAdminUserRequest>
{
    public CreateAdminUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => AdminRoles.All.Contains(r)).WithMessage("Unknown role.");
    }
}

public class UpdateAdminUserRequestValidator : AbstractValidator<UpdateAdminUserRequest>
{
    public UpdateAdminUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100).When(x => x.DisplayName is not null);
        RuleFor(x => x.Role).Must(r => r is null || AdminRoles.All.Contains(r)).WithMessage("Unknown role.");
        RuleFor(x => x).Must(x => x.DisplayName is not null || x.Role is not null)
            .WithName("Request").WithMessage("Nothing to change.");
    }
}

public interface IAdminUsersService
{
    Task<PagedResult<AdminUserDto>> ListAsync(AdminUserQuery query, CancellationToken cancellationToken = default);
    Task<UserCredentialDto> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default);
    Task<AdminUserDto> UpdateAsync(Guid id, UpdateAdminUserRequest request, CancellationToken cancellationToken = default);
    Task<AdminUserDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminUserDto> ReactivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserCredentialDto> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminUserDto> ResetTwoFactorAsync(Guid id, CancellationToken cancellationToken = default);
}

public class AdminUsersService : IAdminUsersService
{
    private readonly MasidBahaDbContext _db;
    private readonly UserManager<AdminUser> _users;
    private readonly IAuditLog _audit;
    private readonly ICurrentAdmin _admin;

    public AdminUsersService(MasidBahaDbContext db, UserManager<AdminUser> users, IAuditLog audit, ICurrentAdmin admin)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _admin = admin;
    }

    public async Task<PagedResult<AdminUserDto>> ListAsync(AdminUserQuery query, CancellationToken cancellationToken = default)
    {
        var users = _users.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + query.Search.Trim() + "%";
            users = users.Where(u => EF.Functions.ILike(u.Email!, pattern) || EF.Functions.ILike(u.DisplayName, pattern));
        }
        if (query.Active is { } active)
            users = users.Where(u => (u.DeactivatedAt == null) == active);
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var inRole = _db.UserRoles.Join(_db.Roles.Where(r => r.Name == query.Role), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId);
            users = users.Where(u => inRole.Contains(u.Id));
        }

        var total = await users.CountAsync(cancellationToken);
        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        var rows = await users.OrderBy(u => u.Email).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        var roles = await RolesForAsync(rows.Select(u => u.Id).ToList(), cancellationToken);

        return new PagedResult<AdminUserDto>
        {
            Items = rows.Select(u => ToDto(u, roles.GetValueOrDefault(u.Id, ""))).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = size
        };
    }

    public async Task<UserCredentialDto> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        if (await _users.FindByEmailAsync(email) is not null)
            throw new ConflictException("A staff account with this email already exists.");

        var password = GenerateTemporaryPassword();
        var user = new AdminUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow,
            MustChangePassword = true
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        Succeeded(await _users.CreateAsync(user, password), "Could not create the account.");
        Succeeded(await _users.AddToRoleAsync(user, request.Role), "Could not assign the role.");

        _audit.Add(AuditActions.UserCreated, "user", user.Id.ToString(), new { email, role = request.Role });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new UserCredentialDto { User = ToDto(user, request.Role), TemporaryPassword = password };
    }

    public async Task<AdminUserDto> UpdateAsync(Guid id, UpdateAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id);
        var currentRole = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var changes = new Dictionary<string, object?>();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (request.DisplayName is not null && request.DisplayName.Trim() != user.DisplayName)
        {
            changes["displayName"] = new { from = user.DisplayName, to = request.DisplayName.Trim() };
            user.DisplayName = request.DisplayName.Trim();
            Succeeded(await _users.UpdateAsync(user), "Could not update the account.");
        }

        if (request.Role is not null && request.Role != currentRole)
        {
            if (user.Id == _admin.Id)
                throw new ConflictException("You cannot change your own role.");
            if (currentRole == AdminRoles.SuperAdmin)
                await EnsureAnotherActiveSuperAdminAsync(user.Id, cancellationToken);

            if (currentRole.Length > 0)
                Succeeded(await _users.RemoveFromRoleAsync(user, currentRole), "Could not change the role.");
            Succeeded(await _users.AddToRoleAsync(user, request.Role), "Could not change the role.");
            // Existing sessions pick up the new role at their next check
            await _users.UpdateSecurityStampAsync(user);

            changes["role"] = new { from = currentRole, to = request.Role };
            currentRole = request.Role;
        }

        if (changes.Count > 0)
            _audit.Add(AuditActions.UserUpdated, "user", user.Id.ToString(), new { email = user.Email, changes });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(user, currentRole);
    }

    public async Task<AdminUserDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id);
        if (user.Id == _admin.Id)
            throw new ConflictException("You cannot deactivate your own account.");

        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        if (role == AdminRoles.SuperAdmin)
            await EnsureAnotherActiveSuperAdminAsync(user.Id, cancellationToken);

        if (user.DeactivatedAt is null)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            user.DeactivatedAt = DateTime.UtcNow;
            Succeeded(await _users.UpdateAsync(user), "Could not deactivate the account.");
            // Ends every session it has open
            await _users.UpdateSecurityStampAsync(user);

            _audit.Add(AuditActions.UserDeactivated, "user", user.Id.ToString(), new { email = user.Email });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ToDto(user, role);
    }

    public async Task<AdminUserDto> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id);
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "";

        if (user.DeactivatedAt is not null)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            user.DeactivatedAt = null;
            Succeeded(await _users.UpdateAsync(user), "Could not reactivate the account.");
            await _users.ResetAccessFailedCountAsync(user);
            await _users.SetLockoutEndDateAsync(user, null);

            _audit.Add(AuditActions.UserReactivated, "user", user.Id.ToString(), new { email = user.Email });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ToDto(user, role);
    }

    public async Task<UserCredentialDto> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id);
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var password = GenerateTemporaryPassword();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        Succeeded(await _users.ResetPasswordAsync(user, token, password), "Could not reset the password.");

        user.MustChangePassword = true;
        Succeeded(await _users.UpdateAsync(user), "Could not reset the password.");
        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);

        _audit.Add(AuditActions.UserPasswordReset, "user", user.Id.ToString(), new { email = user.Email });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new UserCredentialDto { User = ToDto(user, role), TemporaryPassword = password };
    }

    // For someone who lost their authenticator device
    public async Task<AdminUserDto> ResetTwoFactorAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id);
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "";

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.UpdateSecurityStampAsync(user);

        _audit.Add(AuditActions.TwoFactorDisabled, "user", user.Id.ToString(), new { email = user.Email, resetBy = _admin.Email });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(user, role);
    }

    // ---- helpers ----

    private async Task<AdminUser> FindAsync(Guid id) =>
        await _users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("Staff account not found.");

    // Without this the back-office could be left with nobody able to manage accounts
    private async Task EnsureAnotherActiveSuperAdminAsync(Guid excludingUserId, CancellationToken cancellationToken)
    {
        var others = (await _users.GetUsersInRoleAsync(AdminRoles.SuperAdmin))
            .Count(u => u.Id != excludingUserId && u.DeactivatedAt is null);

        if (others == 0)
            throw new ConflictException("There must be at least one active super admin.");
    }

    private async Task<Dictionary<Guid, string>> RolesForAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, Role = r.Name! })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First().Role);
    }

    private static AdminUserDto ToDto(AdminUser u, string role) => new()
    {
        Id = u.Id,
        Email = u.Email ?? "",
        DisplayName = u.DisplayName,
        Role = role,
        IsActive = u.IsActive,
        MustChangePassword = u.MustChangePassword,
        TwoFactorEnabled = u.TwoFactorEnabled,
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt
    };

    private static void Succeeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new ValidationFailedException("Request", $"{message} {string.Join(" ", result.Errors.Select(e => e.Description))}".Trim());
    }

    // 18 random bytes = 24 characters, comfortably above the minimum length
    private static string GenerateTemporaryPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
}
