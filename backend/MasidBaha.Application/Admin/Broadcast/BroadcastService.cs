using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Common;
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
    private readonly IAuditLog _audit;
    private readonly ICurrentAdmin _admin;

    public BroadcastService(MasidBahaDbContext db, IPushQueue push, IAuditLog audit, ICurrentAdmin admin)
    {
        _db = db;
        _push = push;
        _audit = audit;
        _admin = admin;
    }

    public async Task<BroadcastDto> SendAsync(SendBroadcastRequest request, CancellationToken cancellationToken = default)
    {
        var recipients = await _db.PushSubscriptions.CountAsync(cancellationToken);
        var url = string.IsNullOrWhiteSpace(request.Url) ? "/map" : request.Url.Trim();

        var broadcast = new Broadcast
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Url = url,
            SentAt = DateTime.UtcNow,
            SentByEmail = string.IsNullOrEmpty(_admin.Email) ? "system" : _admin.Email,
            Recipients = recipients
        };

        _db.Broadcasts.Add(broadcast);
        _audit.Add(AuditActions.BroadcastSent, "broadcast", broadcast.Id.ToString(), new { broadcast.Title, recipients });
        await _db.SaveChangesAsync(cancellationToken);

        // Delivery happens in the background worker, not in this request
        await _push.EnqueueAsync(new PushPayload { Title = broadcast.Title, Body = broadcast.Body, Url = broadcast.Url });

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
        Id = b.Id, Title = b.Title, Body = b.Body, Url = b.Url, SentAt = b.SentAt, SentByEmail = b.SentByEmail, Recipients = b.Recipients
    };
}
