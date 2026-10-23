using Asp.Versioning;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Persistence;
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
    private const string Issuer = "MasidBaha";

    private readonly UserManager<AdminUser> _users;
    private readonly SignInManager<AdminUser> _signIn;
    private readonly IPasswordHasher<AdminUser> _hasher;
    private readonly IAuditLog _audit;
    private readonly MasidBahaDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AdminUser> users,
        SignInManager<AdminUser> signIn,
        IPasswordHasher<AdminUser> hasher,
        IAuditLog audit,
        MasidBahaDbContext db,
        ILogger<AuthController> logger)
    {
        _users = users;
        _signIn = signIn;
        _hasher = hasher;
        _audit = audit;
        _db = db;
        _logger = logger;
    }

    /// <summary>Signs a staff member in and sets the HttpOnly session cookie. Answers 202 when a two-factor code is still needed.</summary>
    [HttpPost("login")]
    [Consumes("application/json")] // JSON only, so a cross-site form can't post here
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<TwoFactorRequiredDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);

        // Unknown and deactivated accounts look exactly like a wrong password. The hash is
        // computed anyway so response timing doesn't reveal which emails have an account.
        if (user is null || !user.IsActive)
        {
            _hasher.HashPassword(new AdminUser(), request.Password);
            await RecordFailedLoginAsync(request.Email, user, user is null ? "unknown account" : "deactivated account");
            return InvalidCredentials();
        }

        var result = await _signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await RecordFailedLoginAsync(request.Email, user, "locked out");
            return Problem(
                title: "Account temporarily locked",
                detail: "Too many failed attempts. Please try again later.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (result.RequiresTwoFactor)
            return Accepted(new TwoFactorRequiredDto());

        if (!result.Succeeded)
        {
            await RecordFailedLoginAsync(request.Email, user, "wrong password");
            return InvalidCredentials();
        }

        return Ok(await CompleteLoginAsync(user));
    }

    /// <summary>Second step of a sign-in: the code from the authenticator app, or a recovery code.</summary>
    [HttpPost("login/2fa")]
    [Consumes("application/json")]
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> LoginTwoFactor(TwoFactorLoginRequest request)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return Problem(
                title: "Sign-in expired",
                detail: "Please enter your email and password again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = !string.IsNullOrWhiteSpace(request.RecoveryCode)
            ? await _signIn.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode.Trim())
            : await _signIn.TwoFactorAuthenticatorSignInAsync(
                request.Code!.Replace(" ", "").Replace("-", ""), isPersistent: false, rememberClient: false);

        if (result.IsLockedOut)
        {
            await RecordFailedLoginAsync(user.Email!, user, "locked out (two-factor)");
            return Problem(
                title: "Account temporarily locked",
                detail: "Too many failed attempts. Please try again later.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!result.Succeeded)
        {
            await RecordFailedLoginAsync(user.Email!, user, "wrong two-factor code");
            return Problem(title: "Sign-in failed", detail: "That code is not valid.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(await CompleteLoginAsync(user));
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
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
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

        // The temporary password has been replaced, so the account is fully usable now
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await _users.UpdateAsync(user);
        }

        _audit.Add(AuditActions.PasswordChanged, "user", user.Id.ToString());
        await _audit.SaveAsync();

        // Changing the password rotates the security stamp, which signs every other
        // session out. Refresh so this one stays signed in (and loses the "must change" claim).
        await _signIn.RefreshSignInAsync(user);
        return Ok(await ToDtoAsync(user));
    }

    // ---- two-factor setup ----

    /// <summary>Starts setting up an authenticator app: returns the secret to add to it.</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    [ProducesResponseType<TwoFactorSetupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwoFactorSetupDto>> TwoFactorSetup()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (user.TwoFactorEnabled)
            return Problem(title: "Already enabled", detail: "Two-factor sign-in is already on for this account.", statusCode: StatusCodes.Status409Conflict);

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);
        }

        return Ok(new TwoFactorSetupDto
        {
            SharedKey = string.Join(' ', Enumerable.Range(0, (key!.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))),
            OtpAuthUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6"
        });
    }

    /// <summary>Confirms the authenticator app works, turns two-factor on and returns recovery codes (shown once).</summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    [Consumes("application/json")]
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType<TwoFactorEnabledDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TwoFactorEnabledDto>> TwoFactorEnable(TwoFactorEnableRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var valid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "").Replace("-", ""));
        if (!valid)
        {
            ModelState.AddModelError(nameof(request.Code), "That code is not valid. Check the time on your phone and try again.");
            return ValidationProblem(ModelState);
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        _audit.Add(AuditActions.TwoFactorEnabled, "user", user.Id.ToString());
        await _audit.SaveAsync();

        await _signIn.RefreshSignInAsync(user);
        return Ok(new TwoFactorEnabledDto { RecoveryCodes = (codes ?? []).ToList() });
    }

    /// <summary>Turns two-factor off. Needs the password and a current code.</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    [Consumes("application/json")]
    [EnableRateLimiting("auth-attempts")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> TwoFactorDisable(TwoFactorDisableRequest request)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (!await _users.CheckPasswordAsync(user, request.Password))
        {
            ModelState.AddModelError(nameof(request.Password), "Incorrect password.");
            return ValidationProblem(ModelState);
        }

        var valid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "").Replace("-", ""));
        if (!valid)
        {
            ModelState.AddModelError(nameof(request.Code), "That code is not valid.");
            return ValidationProblem(ModelState);
        }

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);

        _audit.Add(AuditActions.TwoFactorDisabled, "user", user.Id.ToString());
        await _audit.SaveAsync();

        await _signIn.RefreshSignInAsync(user);
        return Ok(await ToDtoAsync(user));
    }

    // ---- helpers ----

    private async Task<CurrentUserDto> CompleteLoginAsync(AdminUser user)
    {
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        _audit.AddAnonymous(user.Email!, AuditActions.Login, "user", user.Id.ToString());
        await _audit.SaveAsync();

        return await ToDtoAsync(user);
    }

    private async Task RecordFailedLoginAsync(string email, AdminUser? user, string reason)
    {
        _logger.LogWarning("Failed admin login for {Email}: {Reason}", email, reason);
        _audit.AddAnonymous(email, AuditActions.LoginFailed, "user", user?.Id.ToString(), new { reason });
        await _audit.SaveAsync();
    }

    // One message for "no such account", "deactivated" and "wrong password", so none is revealed
    private ActionResult InvalidCredentials() =>
        Problem(title: "Sign-in failed", detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

    private async Task<CurrentUserDto> ToDtoAsync(AdminUser user) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        DisplayName = user.DisplayName,
        Roles = (await _users.GetRolesAsync(user)).ToList(),
        MustChangePassword = user.MustChangePassword,
        TwoFactorEnabled = user.TwoFactorEnabled
    };
}
