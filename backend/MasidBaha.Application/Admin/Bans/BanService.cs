using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Common;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MasidBaha.Application.Admin.Bans;

public enum BanVerdict
{
    None = 0,
    Shadow = 1, // accept quietly, show to no one
    Block = 2   // refuse
}

public class BanDto
{
    public Guid Id { get; set; }
    public BanKind Kind { get; set; }
    public BanMode Mode { get; set; }

    // A session id in full; an IP only as the start of its hash
    public string Target { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LiftedAt { get; set; }
    public string? LiftedByEmail { get; set; }
    public bool IsActive { get; set; }
}

public class CreateBanRequest
{
    // Normally the report the staff member is looking at: the server then takes the
    // session id or IP hash from that report, so a full hash never leaves the server
    public Guid? ReportId { get; set; }

    public BanKind Kind { get; set; }

    // A session id, for a session ban made without a report
    public string? Value { get; set; }

    public BanMode Mode { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? ExpiresInHours { get; set; }

    // Also pull this source's existing reports off the public map
    public bool HideExistingReports { get; set; }
}

public class BanResult
{
    public BanDto Ban { get; set; } = new();
    public List<Guid> HiddenReportIds { get; set; } = new();
}

public class BanQuery : PagedQuery
{
    public bool? Active { get; set; }
    public BanKind? Kind { get; set; }
}

public class CreateBanRequestValidator : AbstractValidator<CreateBanRequest>
{
    public CreateBanRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ExpiresInHours).InclusiveBetween(1, 24 * 365).When(x => x.ExpiresInHours.HasValue);

        RuleFor(x => x).Must(x => x.ReportId.HasValue || (x.Kind == BanKind.Session && !string.IsNullOrWhiteSpace(x.Value)))
            .WithName("ReportId")
            .WithMessage("Choose a report to ban its source, or give a session id for a session ban.");

        RuleFor(x => x.Value).MaximumLength(128);
    }
}

public class BanQueryValidator : AbstractValidator<BanQuery>
{
    public BanQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public interface IBanService
{
    // Called on every public write. Block wins over Shadow.
    Task<BanVerdict> CheckAsync(string? sessionId, string? ipHash, CancellationToken cancellationToken = default);

    Task<PagedResult<BanDto>> ListAsync(BanQuery query, CancellationToken cancellationToken = default);
    Task<List<BanDto>> ActiveForAsync(string sessionId, string? ipHash, CancellationToken cancellationToken = default);
    Task<BanResult> CreateAsync(CreateBanRequest request, CancellationToken cancellationToken = default);
    Task<BanDto> LiftAsync(Guid id, CancellationToken cancellationToken = default);
}

public class BanService : IBanService
{
    private const string CacheKey = "bans:active";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly MasidBahaDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ICurrentAdmin _admin;
    private readonly IMemoryCache _cache;

    public BanService(MasidBahaDbContext db, IAuditLog audit, ICurrentAdmin admin, IMemoryCache cache)
    {
        _db = db;
        _audit = audit;
        _admin = admin;
        _cache = cache;
    }

    private record ActiveBan(BanKind Kind, string Value, BanMode Mode);

    public async Task<BanVerdict> CheckAsync(string? sessionId, string? ipHash, CancellationToken cancellationToken = default)
    {
        var bans = await GetActiveAsync(cancellationToken);
        var verdict = BanVerdict.None;

        foreach (var ban in bans)
        {
            var matches = ban.Kind == BanKind.Session
                ? !string.IsNullOrEmpty(sessionId) && ban.Value == sessionId
                : !string.IsNullOrEmpty(ipHash) && ban.Value == ipHash;

            if (!matches) continue;

            var candidate = ban.Mode == BanMode.Block ? BanVerdict.Block : BanVerdict.Shadow;
            if (candidate > verdict) verdict = candidate;
        }

        return verdict;
    }

    private async Task<List<ActiveBan>> GetActiveAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out List<ActiveBan>? cached) && cached is not null)
            return cached;

        var now = DateTime.UtcNow;
        var active = await _db.ReporterBans.AsNoTracking()
            .Where(b => b.LiftedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now))
            .Select(b => new ActiveBan(b.Kind, b.Value, b.Mode))
            .ToListAsync(cancellationToken);

        _cache.Set(CacheKey, active, CacheFor);
        return active;
    }

    public async Task<PagedResult<BanDto>> ListAsync(BanQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var bans = _db.ReporterBans.AsNoTracking().AsQueryable();

        if (query.Kind is { } kind) bans = bans.Where(b => b.Kind == kind);
        if (query.Active == true) bans = bans.Where(b => b.LiftedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now));
        if (query.Active == false) bans = bans.Where(b => b.LiftedAt != null || (b.ExpiresAt != null && b.ExpiresAt <= now));

        var total = await bans.CountAsync(cancellationToken);
        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        var rows = await bans.OrderByDescending(b => b.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);

        return new PagedResult<BanDto> { Items = rows.Select(b => ToDto(b, now)).ToList(), TotalCount = total, Page = page, PageSize = size };
    }

    public async Task<List<BanDto>> ActiveForAsync(string sessionId, string? ipHash, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.ReporterBans.AsNoTracking()
            .Where(b => b.LiftedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now))
            .Where(b => (b.Kind == BanKind.Session && b.Value == sessionId) || (ipHash != null && b.Kind == BanKind.Ip && b.Value == ipHash))
            .ToListAsync(cancellationToken);

        return rows.Select(b => ToDto(b, now)).ToList();
    }

    public async Task<BanResult> CreateAsync(CreateBanRequest request, CancellationToken cancellationToken = default)
    {
        string value;
        if (request.ReportId is { } reportId)
        {
            var report = await _db.FloodReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
                ?? throw new NotFoundException("Report not found.");

            value = request.Kind == BanKind.Session ? report.ReporterSessionId : report.ReporterIpHash
                ?? throw new ValidationFailedException("ReportId", "This report has no recorded source address to ban.");
        }
        else
        {
            value = request.Value!.Trim();
        }

        var now = DateTime.UtcNow;
        var alreadyBanned = await _db.ReporterBans.AnyAsync(b =>
            b.Kind == request.Kind && b.Value == value && b.LiftedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), cancellationToken);
        if (alreadyBanned)
            throw new ConflictException("This source is already banned.");

        var ban = new ReporterBan
        {
            Id = Guid.CreateVersion7(),
            Kind = request.Kind,
            Value = value,
            Mode = request.Mode,
            Reason = request.Reason.Trim(),
            CreatedAt = now,
            CreatedByEmail = string.IsNullOrEmpty(_admin.Email) ? "system" : _admin.Email,
            ExpiresAt = request.ExpiresInHours is { } hours ? now.AddHours(hours) : null
        };
        _db.ReporterBans.Add(ban);

        var hidden = new List<Guid>();
        if (request.HideExistingReports)
        {
            var matching = await _db.FloodReports
                .Where(r => !r.IsHidden && (request.Kind == BanKind.Session ? r.ReporterSessionId == value : r.ReporterIpHash == value))
                .ToListAsync(cancellationToken);

            foreach (var report in matching)
            {
                report.IsHidden = true;
                hidden.Add(report.Id);
            }
        }

        _audit.Add(AuditActions.BanCreated, "ban", ban.Id.ToString(), new
        {
            kind = request.Kind.ToString(),
            mode = request.Mode.ToString(),
            target = Mask(request.Kind, value),
            reason = ban.Reason,
            expiresAt = ban.ExpiresAt,
            hiddenReports = hidden.Count
        });

        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(CacheKey);

        return new BanResult { Ban = ToDto(ban, now), HiddenReportIds = hidden };
    }

    public async Task<BanDto> LiftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ban = await _db.ReporterBans.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException("Ban not found.");

        if (ban.LiftedAt is not null)
            throw new ConflictException("This ban has already been lifted.");

        ban.LiftedAt = DateTime.UtcNow;
        ban.LiftedByEmail = string.IsNullOrEmpty(_admin.Email) ? "system" : _admin.Email;

        _audit.Add(AuditActions.BanLifted, "ban", ban.Id.ToString(), new { kind = ban.Kind.ToString(), target = Mask(ban.Kind, ban.Value) });

        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(CacheKey);

        return ToDto(ban, DateTime.UtcNow);
    }

    private static string Mask(BanKind kind, string value) =>
        kind == BanKind.Ip ? value[..Math.Min(10, value.Length)] : value;

    private static BanDto ToDto(ReporterBan b, DateTime now) => new()
    {
        Id = b.Id,
        Kind = b.Kind,
        Mode = b.Mode,
        Target = Mask(b.Kind, b.Value),
        Reason = b.Reason,
        CreatedAt = b.CreatedAt,
        CreatedByEmail = b.CreatedByEmail,
        ExpiresAt = b.ExpiresAt,
        LiftedAt = b.LiftedAt,
        LiftedByEmail = b.LiftedByEmail,
        IsActive = b.IsActive(now)
    };
}
