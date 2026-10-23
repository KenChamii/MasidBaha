using System.Text.Json;
using FluentValidation;
using MasidBaha.Application.Common;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin.Audit;

// The signed-in staff member behind the current request (null outside a request)
public interface ICurrentAdmin
{
    Guid? Id { get; }
    string Email { get; }
}

public class AuditEntryDto
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public JsonElement? Details { get; set; }
}

public class AuditQuery : PagedQuery
{
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class AuditQueryValidator : AbstractValidator<AuditQuery>
{
    public AuditQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Action).MaximumLength(64);
        RuleFor(x => x.Actor).MaximumLength(256);
        RuleFor(x => x.TargetType).MaximumLength(32);
        RuleFor(x => x.TargetId).MaximumLength(64);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}

public interface IAuditLog
{
    // Adds an entry to the current unit of work. It is saved together with the
    // change it describes (the caller's SaveChangesAsync), so the two can never disagree.
    void Add(string action, string targetType, string? targetId = null, object? details = null);

    // Same, for someone who is not signed in (e.g. a failed login attempt)
    void AddAnonymous(string actorLabel, string action, string targetType, string? targetId = null, object? details = null);

    // For callers with no other change to save (logins, 2FA changes)
    Task SaveAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

public class AuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MasidBahaDbContext _db;
    private readonly ICurrentAdmin _admin;

    public AuditLog(MasidBahaDbContext db, ICurrentAdmin admin)
    {
        _db = db;
        _admin = admin;
    }

    public void Add(string action, string targetType, string? targetId = null, object? details = null) =>
        AddEntry(_admin.Id, string.IsNullOrEmpty(_admin.Email) ? "system" : _admin.Email, action, targetType, targetId, details);

    public void AddAnonymous(string actorLabel, string action, string targetType, string? targetId = null, object? details = null) =>
        AddEntry(null, actorLabel, action, targetType, targetId, details);

    public Task SaveAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);

    private void AddEntry(Guid? actorId, string actorEmail, string action, string targetType, string? targetId, object? details)
    {
        _db.AuditLog.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            At = DateTime.UtcNow,
            ActorId = actorId,
            ActorEmail = actorEmail.Length > 256 ? actorEmail[..256] : actorEmail,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details is null ? null : JsonSerializer.Serialize(details, Json)
        });
    }

    public async Task<PagedResult<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var entries = _db.AuditLog.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Action))
            entries = entries.Where(e => e.Action.StartsWith(query.Action));
        if (!string.IsNullOrWhiteSpace(query.Actor))
            entries = entries.Where(e => EF.Functions.ILike(e.ActorEmail, "%" + query.Actor + "%"));
        if (!string.IsNullOrWhiteSpace(query.TargetType))
            entries = entries.Where(e => e.TargetType == query.TargetType);
        if (!string.IsNullOrWhiteSpace(query.TargetId))
            entries = entries.Where(e => e.TargetId == query.TargetId);
        if (query.From is { } from)
            entries = entries.Where(e => e.At >= from.ToUtc());
        if (query.To is { } to)
            entries = entries.Where(e => e.At <= to.ToUtc());

        var total = await entries.CountAsync(cancellationToken);
        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        var rows = await entries
            .OrderByDescending(e => e.At)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryDto>
        {
            Items = rows.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = size
        };
    }

    public static AuditEntryDto ToDto(AuditEntry e) => new()
    {
        Id = e.Id,
        At = e.At,
        ActorEmail = e.ActorEmail,
        Action = e.Action,
        TargetType = e.TargetType,
        TargetId = e.TargetId,
        Details = e.Details is null ? null : JsonDocument.Parse(e.Details).RootElement.Clone()
    };
}
