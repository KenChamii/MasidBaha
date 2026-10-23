using Asp.Versioning;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Admin.Users;
using MasidBaha.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasidBaha.WebAPI.Controllers;

// Staff management. SuperAdmin only; there is no public sign-up.
[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = AdminPolicies.ManageUsers)]
[Route("api/v{version:apiVersion}/admin/users")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUsersService _users;

    public AdminUsersController(IAdminUsersService users) => _users = users;

    /// <summary>Paged list of staff accounts.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List([FromQuery] AdminUserQuery query, CancellationToken cancellationToken) =>
        Ok(await _users.ListAsync(query, cancellationToken));

    /// <summary>Creates a staff account with a temporary password, shown once, that must be changed at first sign-in.</summary>
    [HttpPost]
    [ProducesResponseType<UserCredentialDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserCredentialDto>> Create(CreateAdminUserRequest request, CancellationToken cancellationToken)
    {
        var created = await _users.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Changes a staff member's name or role.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> Update(Guid id, UpdateAdminUserRequest request, CancellationToken cancellationToken) =>
        Ok(await _users.UpdateAsync(id, request, cancellationToken));

    /// <summary>Deactivates an account and signs it out. Never the last active SuperAdmin, never yourself.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        Ok(await _users.DeactivateAsync(id, cancellationToken));

    /// <summary>Re-enables a deactivated account.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserDto>> Reactivate(Guid id, CancellationToken cancellationToken) =>
        Ok(await _users.ReactivateAsync(id, cancellationToken));

    /// <summary>Sets a new temporary password (shown once) and signs the account out everywhere.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<UserCredentialDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserCredentialDto>> ResetPassword(Guid id, CancellationToken cancellationToken) =>
        Ok(await _users.ResetPasswordAsync(id, cancellationToken));

    /// <summary>Turns off two-factor sign-in for someone who lost their phone.</summary>
    [HttpPost("{id:guid}/reset-2fa")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserDto>> ResetTwoFactor(Guid id, CancellationToken cancellationToken) =>
        Ok(await _users.ResetTwoFactorAsync(id, cancellationToken));
}
