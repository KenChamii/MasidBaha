using Asp.Versioning;
using MasidBaha.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MasidBaha.WebAPI.Controllers;

// Back-office login. Staff accounts are created by a SuperAdmin (or seeded on
// first start); there is no public sign-up.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AdminUser> _users;
    private readonly SignInManager<AdminUser> _signIn;
    private readonly IPasswordHasher<AdminUser> _hasher;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AdminUser> users,
        SignInManager<AdminUser> signIn,
        IPasswordHasher<AdminUser> hasher,
        ILogger<AuthController> logger)
    {
        _users = users;
        _signIn = signIn;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>Signs a staff member in and sets the HttpOnly session cookie.</summary>
    [HttpPost("login")]
    [Consumes("application/json")] // JSON only, so a cross-site form can't post here
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);

        if (user is null)
        {
            // Spend the same time hashing as a real check would, so response
            // timing doesn't reveal which emails have an account
            _hasher.HashPassword(new AdminUser(), request.Password);
            return InvalidCredentials();
        }

        var result = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Admin login blocked, account locked out: {Email}", request.Email);
            return Problem(
                title: "Account temporarily locked",
                detail: "Too many failed attempts. Please try again later.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed admin login for {Email}", request.Email);
            return InvalidCredentials();
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await _signIn.SignInAsync(user, isPersistent: false);

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Ends the session. Safe to call when not signed in.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>The signed-in staff member, so the admin app can restore its session.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await _users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(await ToDtoAsync(user));
    }

    /// <summary>Changes the signed-in staff member's own password.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [Consumes("application/json")]
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                    ? nameof(request.CurrentPassword)
                    : nameof(request.NewPassword);
                ModelState.AddModelError(field, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        // Changing the password rotates the security stamp, which signs every
        // other session out. Refresh so this one stays signed in.
        await _signIn.RefreshSignInAsync(user);
        return NoContent();
    }

    // One message for "no such account" and "wrong password", so neither is revealed
    private ActionResult InvalidCredentials() =>
        Problem(title: "Sign-in failed", detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

    private async Task<CurrentUserDto> ToDtoAsync(AdminUser user) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        DisplayName = user.DisplayName,
        Roles = (await _users.GetRolesAsync(user)).ToList()
    };
}
