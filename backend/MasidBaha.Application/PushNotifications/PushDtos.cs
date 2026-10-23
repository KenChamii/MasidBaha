using FluentValidation;

namespace MasidBaha.Application.PushNotifications;

// Mirrors the shape of the browser PushSubscription object (subscription.toJSON())
// so the frontend can post it more or less as-is.
public class PushSubscriptionDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public PushSubscriptionKeysDto Keys { get; set; } = new();
}

public class PushSubscriptionKeysDto
{
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public class UnsubscribeRequest
{
    public string Endpoint { get; set; } = string.Empty;
}

public class PushPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    // Where to navigate when the notification is tapped (e.g. "/map").
    public string Url { get; set; } = "/map";
}

public class PushSubscriptionDtoValidator : AbstractValidator<PushSubscriptionDto>
{
    public PushSubscriptionDtoValidator()
    {
        // A push endpoint is always an https URL issued by the browser vendor's push service
        RuleFor(x => x.Endpoint)
            .NotEmpty()
            .MaximumLength(500)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Endpoint must be an https URL.");

        RuleFor(x => x.Keys.P256dh).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Keys.Auth).NotEmpty().MaximumLength(500);
    }
}

public class UnsubscribeRequestValidator : AbstractValidator<UnsubscribeRequest>
{
    public UnsubscribeRequestValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(500);
    }
}
