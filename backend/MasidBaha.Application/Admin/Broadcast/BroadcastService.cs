using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Common;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.PushNotifications;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin.Broadcasts;

public class SendBroadcastRequest
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // In-app path opened when the notification is tapped
    public string? Url { get; set; }

    // Optional: send only to people who watch a place within this circle (all three, or none)
    public double? AreaLat { get; set; }
    public double? AreaLng { get; set; }
    public int? AreaRadiusMeters { get; set; }
}

public class BroadcastDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string SentByEmail { get; set; } = string.Empty;
    public int Recipients { get; set; }
    public double? AreaLat { get; set; }
    public double? AreaLng { get; set; }
    public int? AreaRadiusMeters { get; set; }
}

public class SendBroadcastRequestValidator : AbstractValidator<SendBroadcastRequest>
{
    public SendBroadcastRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(300);
        // Only in-app paths: an announcement must not be able to send people to another site
        RuleFor(x => x.Url)
            .Must(u => string.IsNullOrEmpty(u) || (u.StartsWith('/') && !u.StartsWith("//") && !u.Contains('\\') && u.Length <= 200))
            .WithMessage("Url must be a path inside the app, such as /map.");

        RuleFor(x => x).Must(x => (x.AreaLat is null) == (x.AreaLng is null) && (x.AreaLat is null) == (x.AreaRadiusMeters is null))
            .WithName("Area").WithMessage("Give the area's latitude, longitude and radius together, or none of them.");
        RuleFor(x => x.AreaLat).InclusiveBetween(PhilippinesBounds.MinLat, PhilippinesBounds.MaxLat).When(x => x.AreaLat.HasValue);
        RuleFor(x => x.AreaLng).InclusiveBetween(PhilippinesBounds.MinLng, PhilippinesBounds.MaxLng).When(x => x.AreaLng.HasValue);
        RuleFor(x => x.AreaRadiusMeters).InclusiveBetween(1000, 300_000).When(x => x.AreaRadiusMeters.HasValue);
    }
}

public interface IBroadcastService
{
    Task<BroadcastDto> SendAsync(SendBroadcastRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<BroadcastDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default);
}

public class BroadcastService : IBroadcastService
{
    private readonly MasidBahaDbContext _db;
    private readonly IPushQueue _push;
    private readonly IPushSubscriptionService _subscriptions;
    private readonly IAuditLog _audit;
    private readonly ICurrentAdmin _admin;

    public BroadcastService(MasidBahaDbContext db, IPushQueue push, IPushSubscriptionService subscriptions, IAuditLog audit, ICurrentAdmin admin)
    {
        _db = db;
        _push = push;
        _subscriptions = subscriptions;
        _audit = audit;
        _admin = admin;
    }

    public async Task<BroadcastDto> SendAsync(SendBroadcastRequest request, CancellationToken cancellationToken = default)
    {
        // With an area, only people watching somewhere inside it; without one, everybody
        var recipients = await _subscriptions.CountRecipientsAsync(request.AreaLat, request.AreaLng, request.AreaRadiusMeters, cancellationToken);
        var url = string.IsNullOrWhiteSpace(request.Url) ? "/map" : request.Url.Trim();

        var broadcast = new Broadcast
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Url = url,
            SentAt = DateTime.UtcNow,
            SentByEmail = string.IsNullOrEmpty(_admin.Email) ? "system" : _admin.Email,
            Recipients = recipients,
            AreaLat = request.AreaLat,
            AreaLng = request.AreaLng,
            AreaRadiusMeters = request.AreaRadiusMeters
        };

        _db.Broadcasts.Add(broadcast);
        _audit.Add(AuditActions.BroadcastSent, "broadcast", broadcast.Id.ToString(), new { broadcast.Title, recipients, area = request.AreaRadiusMeters is null ? null : new { request.AreaLat, request.AreaLng, request.AreaRadiusMeters } });
        await _db.SaveChangesAsync(cancellationToken);

        // Delivery happens in the background worker, not in this request
        await _push.EnqueueAsync(new PushPayload
        {
            Title = broadcast.Title,
            Body = broadcast.Body,
            Url = broadcast.Url,
            Lat = request.AreaLat,
            Lng = request.AreaLng,
            RadiusMeters = request.AreaRadiusMeters
        });

        return ToDto(broadcast);
    }

    public async Task<PagedResult<BroadcastDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var total = await _db.Broadcasts.CountAsync(cancellationToken);
        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        var rows = await _db.Broadcasts.AsNoTracking()
            .OrderByDescending(b => b.SentAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new PagedResult<BroadcastDto> { Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = page, PageSize = size };
    }

    private static BroadcastDto ToDto(Broadcast b) => new()
    {
        Id = b.Id, Title = b.Title, Body = b.Body, Url = b.Url, SentAt = b.SentAt, SentByEmail = b.SentByEmail, Recipients = b.Recipients,
        AreaLat = b.AreaLat, AreaLng = b.AreaLng, AreaRadiusMeters = b.AreaRadiusMeters
    };
}
