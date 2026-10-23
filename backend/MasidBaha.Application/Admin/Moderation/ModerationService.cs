using FluentValidation;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Common;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin.Moderation;

// Why a report was put in front of a moderator
public static class ReportFlags
{
    public const string LowTrustReporter = "low_trust_reporter"; // this session's reports rarely get confirmed
    public const string ManyDenies = "many_denies";              // the community keeps saying "it's gone"
    public const string ReporterFlooding = "reporter_flooding";  // one session posting a lot, fast
    public const string Hidden = "hidden";                       // from a shadow-banned source
}

public class ReportAssessment
{
    public List<string> Flags { get; set; } = new();
    public int DenyVotes { get; set; }
    public int ConfirmVotes { get; set; }
}

public class ModerationQueueItemDto
{
    public AdminFloodReportDto Report { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public int DenyVotes { get; set; }
    public int ConfirmVotes { get; set; }
}

public enum BulkAction
{
    Resolve = 1,
    Expire = 2,
    Reactivate = 3,
    Delete = 4,
    Hide = 5,
    Unhide = 6
}

public class BulkReportActionRequest
{
    public List<Guid> Ids { get; set; } = new();
    public BulkAction Action { get; set; }
}

public class BulkActionResult
{
    public BulkAction Action { get; set; }
    public List<Guid> AffectedIds { get; set; } = new();
    public List<Guid> NotFoundIds { get; set; } = new();
}

public class BulkReportActionRequestValidator : AbstractValidator<BulkReportActionRequest>
{
    public BulkReportActionRequestValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Ids).NotEmpty().Must(ids => ids.Count <= 200).WithMessage("At most 200 reports at a time.");
        RuleFor(x => x.Ids).Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("The same report is listed twice.");
    }
}

public interface IModerationService
{
    Task<Dictionary<Guid, ReportAssessment>> AssessAsync(IReadOnlyCollection<FloodReport> reports, CancellationToken cancellationToken = default);
    Task<PagedResult<ModerationQueueItemDto>> GetQueueAsync(PagedQuery query, CancellationToken cancellationToken = default);
    Task<int> CountFlaggedAsync(CancellationToken cancellationToken = default);
    Task<BulkActionResult> ApplyBulkAsync(BulkReportActionRequest request, CancellationToken cancellationToken = default);
}

public class ModerationService : IModerationService
{
    // How many of the newest live reports are checked for flags
    private const int CandidateLimit = 500;

    private readonly MasidBahaDbContext _db;
    private readonly IRuntimeSettings _settings;
    private readonly IAuditLog _audit;

    public ModerationService(MasidBahaDbContext db, IRuntimeSettings settings, IAuditLog audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<Dictionary<Guid, ReportAssessment>> AssessAsync(
        IReadOnlyCollection<FloodReport> reports, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        var reportIds = reports.Select(r => r.Id).ToList();
        var sessionIds = reports.Select(r => r.ReporterSessionId).Distinct().ToList();

        var votes = await _db.ReportVotes.AsNoTracking()
            .Where(v => reportIds.Contains(v.FloodReportId))
            .GroupBy(v => new { v.FloodReportId, v.VoteType })
            .Select(g => new { g.Key.FloodReportId, g.Key.VoteType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var sessions = await _db.FloodReports.AsNoTracking()
            .Where(r => sessionIds.Contains(r.ReporterSessionId))
            .GroupBy(r => r.ReporterSessionId)
            .Select(g => new
            {
                Session = g.Key,
                Total = g.Count(),
                Resolved = g.Count(r => r.Status == ReportStatus.Resolved),
                LastHour = g.Count(r => r.ReportedAt >= hourAgo)
            })
            .ToDictionaryAsync(s => s.Session, cancellationToken);

        var result = new Dictionary<Guid, ReportAssessment>();
        foreach (var report in reports)
        {
            var assessment = new ReportAssessment
            {
                DenyVotes = votes.Where(v => v.FloodReportId == report.Id && v.VoteType == VoteType.Deny).Sum(v => v.Count),
                ConfirmVotes = votes.Where(v => v.FloodReportId == report.Id && v.VoteType == VoteType.Confirm).Sum(v => v.Count)
            };

            if (assessment.DenyVotes >= settings.DenyFlagThreshold)
                assessment.Flags.Add(ReportFlags.ManyDenies);

            if (sessions.TryGetValue(report.ReporterSessionId, out var stats))
            {
                // Needs a few reports before judging: one unconfirmed report proves nothing
                if (stats.Total >= 3 && stats.Resolved * 4 < stats.Total)
                    assessment.Flags.Add(ReportFlags.LowTrustReporter);

                if (stats.LastHour >= settings.FloodingReportsPerHour)
                    assessment.Flags.Add(ReportFlags.ReporterFlooding);
            }

            if (report.IsHidden)
                assessment.Flags.Add(ReportFlags.Hidden);

            result[report.Id] = assessment;
        }

        return result;
    }

    private async Task<List<(FloodReport Report, ReportAssessment Assessment)>> GetFlaggedAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var candidates = await _db.FloodReports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Active && r.ExpiresAt > now)
            .OrderByDescending(r => r.ReportedAt)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);

        var assessments = await AssessAsync(candidates, cancellationToken);

        return candidates
            .Select(r => (Report: r, Assessment: assessments[r.Id]))
            .Where(x => x.Assessment.Flags.Count > 0)
            .OrderByDescending(x => x.Assessment.Flags.Count)
            .ThenByDescending(x => x.Report.ReportedAt)
            .ToList();
    }

    public async Task<PagedResult<ModerationQueueItemDto>> GetQueueAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var flagged = await GetFlaggedAsync(cancellationToken);
        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        return new PagedResult<ModerationQueueItemDto>
        {
            Items = flagged.Skip((page - 1) * size).Take(size).Select(x => new ModerationQueueItemDto
            {
                Report = x.Report.ToAdminDto(),
                Flags = x.Assessment.Flags,
                DenyVotes = x.Assessment.DenyVotes,
                ConfirmVotes = x.Assessment.ConfirmVotes
            }).ToList(),
            TotalCount = flagged.Count,
            Page = page,
            PageSize = size
        };
    }

    public async Task<int> CountFlaggedAsync(CancellationToken cancellationToken = default) =>
        (await GetFlaggedAsync(cancellationToken)).Count;

    public async Task<BulkActionResult> ApplyBulkAsync(BulkReportActionRequest request, CancellationToken cancellationToken = default)
    {
        var reports = await _db.FloodReports.Where(r => request.Ids.Contains(r.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var lifetime = _settings.Current.ReportLifetime;

        foreach (var report in reports)
        {
            switch (request.Action)
            {
                case BulkAction.Resolve: report.Status = ReportStatus.Resolved; break;
                case BulkAction.Expire: report.Status = ReportStatus.Expired; break;
                case BulkAction.Reactivate: Reactivate(report, now, lifetime); break;
                case BulkAction.Hide: report.IsHidden = true; break;
                case BulkAction.Unhide: report.IsHidden = false; break;
                case BulkAction.Delete: _db.FloodReports.Remove(report); break; // votes go with it (ON DELETE CASCADE)
            }
        }

        var found = reports.Select(r => r.Id).ToHashSet();
        var result = new BulkActionResult
        {
            Action = request.Action,
            AffectedIds = reports.Select(r => r.Id).ToList(),
            NotFoundIds = request.Ids.Where(id => !found.Contains(id)).ToList()
        };

        _audit.Add(AuditActions.ReportsBulk, "report", details: new
        {
            action = request.Action.ToString(),
            count = result.AffectedIds.Count,
            ids = result.AffectedIds
        });

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    // An expired report whose time has run out would stay invisible even when set back to
    // Active, so reactivating also gives it a fresh lifetime.
    public static void Reactivate(FloodReport report, DateTime now, TimeSpan lifetime)
    {
        report.Status = ReportStatus.Active;
        if (report.ExpiresAt <= now)
            report.ExpiresAt = now + lifetime;
    }
}
