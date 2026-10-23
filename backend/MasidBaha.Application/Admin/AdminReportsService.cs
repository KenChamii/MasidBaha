using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Admin.Moderation;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.Trust;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin;

public interface IAdminReportsService
{
    Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query, CancellationToken cancellationToken = default);
    Task<AdminReportDetailDto> GetDetailAsync(Guid floodReportId, CancellationToken cancellationToken = default);
    Task<List<AdminMapPointDto>> GetMapPointsAsync(AdminReportFilter filter, CancellationToken cancellationToken = default);

    // Streams matching reports (capped) for the CSV export, and records that it happened
    Task<IAsyncEnumerable<AdminFloodReportDto>> ExportAsync(AdminReportFilter filter, CancellationToken cancellationToken = default);

    Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status, CancellationToken cancellationToken = default);
    Task<bool> SetHiddenAsync(Guid floodReportId, bool hidden, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid floodReportId, CancellationToken cancellationToken = default);
}

public class AdminReportsService : IAdminReportsService
{
    public const int ExportLimit = 10_000;
    private const int MapLimit = 2_000;

    private readonly MasidBahaDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IModerationService _moderation;
    private readonly IBanService _bans;
    private readonly ISessionTrustService _trust;
    private readonly IRuntimeSettings _settings;

    public AdminReportsService(
        MasidBahaDbContext db,
        IAuditLog audit,
        IModerationService moderation,
        IBanService bans,
        ISessionTrustService trust,
        IRuntimeSettings settings)
    {
        _db = db;
        _audit = audit;
        _moderation = moderation;
        _bans = bans;
        _trust = trust;
        _settings = settings;
    }

    public async Task<AdminGetReportsResult> GetReportsAsync(AdminGetReportsQuery query, CancellationToken cancellationToken = default)
    {
        var reports = ApplyFilter(_db.FloodReports.AsNoTracking(), query);
        var total = await reports.CountAsync(cancellationToken);

        var page = query.NormalisedPage;
        var size = query.NormalisedPageSize;

        var items = await ApplySort(reports, query)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new AdminGetReportsResult
        {
            Reports = items.Select(r => r.ToAdminDto()).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = size
        };
    }

    public async Task<AdminReportDetailDto> GetDetailAsync(Guid floodReportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.FloodReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == floodReportId, cancellationToken)
            ?? throw new NotFoundException("Report not found.");

        var votes = await _db.ReportVotes.AsNoTracking()
            .Where(v => v.FloodReportId == floodReportId)
            .OrderByDescending(v => v.VotedAt)
            .Select(v => new AdminVoteDto { VoterSessionId = v.VoterSessionId, VoteType = v.VoteType, VotedAt = v.VotedAt })
            .ToListAsync(cancellationToken);

        var assessment = (await _moderation.AssessAsync([report], cancellationToken))[report.Id];

        var history = await _db.AuditLog.AsNoTracking()
            .Where(e => e.TargetType == "report" && e.TargetId == floodReportId.ToString())
            .OrderByDescending(e => e.At)
            .Take(30)
            .ToListAsync(cancellationToken);

        return new AdminReportDetailDto
        {
            Report = report.ToAdminDto(),
            Votes = votes,
            ReporterTrust = await _trust.GetTrustScoreAsync(report.ReporterSessionId),
            Flags = assessment.Flags,
            ActiveBans = await _bans.ActiveForAsync(report.ReporterSessionId, report.ReporterIpHash, cancellationToken),
            History = history.Select(AuditLog.ToDto).ToList()
        };
    }

    public async Task<List<AdminMapPointDto>> GetMapPointsAsync(AdminReportFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await ApplyFilter(_db.FloodReports.AsNoTracking(), filter)
            .OrderByDescending(r => r.ReportedAt)
            .Take(MapLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AdminMapPointDto
        {
            Id = r.Id,
            Lat = r.Location.Y,
            Lng = r.Location.X,
            Severity = r.Severity,
            Status = r.Status,
            ConfidenceScore = r.ConfidenceScore,
            ReportedAt = r.ReportedAt,
            City = r.City,
            IsHidden = r.IsHidden
        }).ToList();
    }

    public async Task<IAsyncEnumerable<AdminFloodReportDto>> ExportAsync(AdminReportFilter filter, CancellationToken cancellationToken = default)
    {
        // Leaving a trail of who took data out is the point of auditing an export
        _audit.Add(AuditActions.ReportsExported, "report", details: new
        {
            filter.Search, filter.Status, filter.Severity, filter.Region, filter.Province, filter.City, filter.From, filter.To
        });
        await _db.SaveChangesAsync(cancellationToken);

        var rows = ApplyFilter(_db.FloodReports.AsNoTracking(), filter)
            .OrderByDescending(r => r.ReportedAt)
            .Take(ExportLimit)
            .AsAsyncEnumerable();

        return Project(rows);

        static async IAsyncEnumerable<AdminFloodReportDto> Project(IAsyncEnumerable<FloodReport> source)
        {
            await foreach (var report in source)
                yield return report.ToAdminDto();
        }
    }

    public async Task<AdminStatusResult> SetStatusAsync(Guid floodReportId, ReportStatus status, CancellationToken cancellationToken = default)
    {
        var report = await _db.FloodReports.FirstOrDefaultAsync(r => r.Id == floodReportId, cancellationToken)
            ?? throw new NotFoundException("Report not found.");

        var before = report.Status;
        if (status == ReportStatus.Active)
            ModerationService.Reactivate(report, DateTime.UtcNow, _settings.Current.ReportLifetime);
        else
            report.Status = status;

        _audit.Add(AuditActions.ReportStatusChanged, "report", report.Id.ToString(), new { from = before.ToString(), to = status.ToString() });
        await _db.SaveChangesAsync(cancellationToken);

        return new AdminStatusResult { Status = report.Status, ConfidenceScore = report.ConfidenceScore };
    }

    public async Task<bool> SetHiddenAsync(Guid floodReportId, bool hidden, CancellationToken cancellationToken = default)
    {
        var report = await _db.FloodReports.FirstOrDefaultAsync(r => r.Id == floodReportId, cancellationToken);
        if (report is null) return false;

        report.IsHidden = hidden;
        _audit.Add(AuditActions.ReportStatusChanged, "report", report.Id.ToString(), new { hidden });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Votes go with the report through the FK's ON DELETE CASCADE
    public async Task<bool> DeleteAsync(Guid floodReportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.FloodReports.FirstOrDefaultAsync(r => r.Id == floodReportId, cancellationToken);
        if (report is null) return false;

        _audit.Add(AuditActions.ReportDeleted, "report", report.Id.ToString(), new
        {
            severity = report.Severity.ToString(),
            city = report.City,
            reporter = report.ReporterSessionId,
            reportedAt = report.ReportedAt
        });
        _db.FloodReports.Remove(report);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IQueryable<FloodReport> ApplyFilter(IQueryable<FloodReport> reports, AdminReportFilter f)
    {
        if (f.Status is { } status) reports = reports.Where(r => r.Status == status);
        if (f.Severity is { } severity) reports = reports.Where(r => r.Severity == severity);
        if (!string.IsNullOrWhiteSpace(f.Region)) reports = reports.Where(r => r.Region == f.Region);
        if (!string.IsNullOrWhiteSpace(f.Province)) reports = reports.Where(r => r.Province == f.Province);
        if (!string.IsNullOrWhiteSpace(f.City)) reports = reports.Where(r => r.City == f.City);
        if (f.From is { } from) reports = reports.Where(r => r.ReportedAt >= from.ToUtc());
        if (f.To is { } to) reports = reports.Where(r => r.ReportedAt <= to.ToUtc());
        if (f.Hidden is { } hidden) reports = reports.Where(r => r.IsHidden == hidden);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var pattern = "%" + EscapeLike(f.Search.Trim()) + "%";
            reports = reports.Where(r =>
                EF.Functions.ILike(r.Notes ?? "", pattern) ||
                EF.Functions.ILike(r.City ?? "", pattern) ||
                EF.Functions.ILike(r.Province ?? "", pattern) ||
                EF.Functions.ILike(r.Region ?? "", pattern) ||
                EF.Functions.ILike(r.ReporterSessionId, pattern));
        }

        return reports;
    }

    private static IQueryable<FloodReport> ApplySort(IQueryable<FloodReport> reports, AdminGetReportsQuery q)
    {
        var desc = q.Descending;
        var sorted = (q.SortBy?.ToLowerInvariant()) switch
        {
            "confidence" => desc ? reports.OrderByDescending(r => r.ConfidenceScore) : reports.OrderBy(r => r.ConfidenceScore),
            "severity" => desc ? reports.OrderByDescending(r => r.Severity) : reports.OrderBy(r => r.Severity),
            "status" => desc ? reports.OrderByDescending(r => r.Status) : reports.OrderBy(r => r.Status),
            "expiresat" => desc ? reports.OrderByDescending(r => r.ExpiresAt) : reports.OrderBy(r => r.ExpiresAt),
            _ => desc ? reports.OrderByDescending(r => r.ReportedAt) : reports.OrderBy(r => r.ReportedAt)
        };

        // A stable tiebreaker, so paging never repeats or skips rows
        return sorted.ThenBy(r => r.Id);
    }

    // % and _ are wildcards in LIKE; a search for "50%" must mean the characters, not "anything"
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
