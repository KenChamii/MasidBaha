using FluentValidation;

namespace MasidBaha.Application.Admin;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CurrentUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();

    // True until a new or reset account replaces its temporary password
    public bool MustChangePassword { get; set; }

    public bool TwoFactorEnabled { get; set; }
}

// Answer to a password that was right for an account with two-factor turned on:
// the browser must now send the authenticator code (or a recovery code)
public class TwoFactorRequiredDto
{
    public bool RequiresTwoFactor { get; set; } = true;
}

public class TwoFactorLoginRequest
{
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}

public class TwoFactorSetupDto
{
    // The secret to type or scan into an authenticator app, in groups for readability
    public string SharedKey { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
}

public class TwoFactorEnableRequest
{
    public string Code { get; set; } = string.Empty;
}

public class TwoFactorEnabledDto
{
    // Shown once: each works a single time if the phone is lost
    public List<string> RecoveryCodes { get; set; } = new();
}

public class TwoFactorDisableRequest
{
    public string Password { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // The length cap stops absurdly long inputs from being fed to the password hasher
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);
        // Strength rules live in the Identity options, which report their own errors
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(200);
    }
}

public class TwoFactorLoginRequestValidator : AbstractValidator<TwoFactorLoginRequest>
{
    public TwoFactorLoginRequestValidator()
    {
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Code) || !string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithName("Code").WithMessage("Enter the code from your authenticator app, or a recovery code.");
        RuleFor(x => x.Code).MaximumLength(12);
        RuleFor(x => x.RecoveryCode).MaximumLength(32);
    }
}

public class TwoFactorEnableRequestValidator : AbstractValidator<TwoFactorEnableRequest>
{
    public TwoFactorEnableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(12);
    }
}

public class TwoFactorDisableRequestValidator : AbstractValidator<TwoFactorDisableRequest>
{
    public TwoFactorDisableRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(12);
    }
}
