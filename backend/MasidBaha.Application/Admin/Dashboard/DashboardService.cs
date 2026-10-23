using MasidBaha.Application.Admin.Moderation;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Admin.Dashboard;

public class SeverityCountDto
{
    public Severity Severity { get; set; }
    public int Count { get; set; }
}

public class PlaceCountDto
{
    public string Place { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DayCountDto
{
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}

public class DashboardDto
{
    public DateTime GeneratedAt { get; set; }

    public int ActiveReports { get; set; }
    public int ReportsLast24h { get; set; }
    public int ReportsPrevious24h { get; set; }
    public int ReportsLast7d { get; set; }
    public int ReportsPrevious7d { get; set; }
    public int VotesLast24h { get; set; }
    public int PushSubscribers { get; set; }
    public int FlaggedReports { get; set; }

    // Of the reports that have closed in the last 30 days, the share the community
    // resolved (confirmed handled) rather than let expire. Null when none closed.
    public double? ResolvedShare30d { get; set; }

    public List<SeverityCountDto> SeverityBreakdown30d { get; set; } = new();
    public List<PlaceCountDto> TopCities30d { get; set; } = new();
    public List<DayCountDto> DailyReports30d { get; set; } = new();
}

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

public class DashboardService : IDashboardService
{
    private const int Days = 30;

    private readonly MasidBahaDbContext _db;
    private readonly IModerationService _moderation;

    public DashboardService(MasidBahaDbContext db, IModerationService moderation)
    {
        _db = db;
        _moderation = moderation;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var twoDays = now.AddDays(-2);
        var week = now.AddDays(-7);
        var twoWeeks = now.AddDays(-14);
        var month = now.AddDays(-Days);

        // Hidden reports are junk by definition, so they don't count towards the numbers
        var visible = _db.FloodReports.AsNoTracking().Where(r => !r.IsHidden);

        var dto = new DashboardDto { GeneratedAt = now };

        dto.ActiveReports = await visible.CountAsync(r => r.Status == ReportStatus.Active && r.ExpiresAt > now, cancellationToken);
        dto.ReportsLast24h = await visible.CountAsync(r => r.ReportedAt >= day, cancellationToken);
        dto.ReportsPrevious24h = await visible.CountAsync(r => r.ReportedAt >= twoDays && r.ReportedAt < day, cancellationToken);
        dto.ReportsLast7d = await visible.CountAsync(r => r.ReportedAt >= week, cancellationToken);
        dto.ReportsPrevious7d = await visible.CountAsync(r => r.ReportedAt >= twoWeeks && r.ReportedAt < week, cancellationToken);
        dto.VotesLast24h = await _db.ReportVotes.AsNoTracking().CountAsync(v => v.VotedAt >= day, cancellationToken);
        dto.PushSubscribers = await _db.PushSubscriptions.AsNoTracking().CountAsync(cancellationToken);
        dto.FlaggedReports = await _moderation.CountFlaggedAsync(cancellationToken);

        var closed = await visible
            .Where(r => r.ReportedAt >= month && (r.Status == ReportStatus.Resolved || r.Status == ReportStatus.Expired))
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var resolved = closed.FirstOrDefault(c => c.Status == ReportStatus.Resolved)?.Count ?? 0;
        var closedTotal = closed.Sum(c => c.Count);
        dto.ResolvedShare30d = closedTotal == 0 ? null : Math.Round(resolved * 100.0 / closedTotal, 1);

        var severities = await visible
            .Where(r => r.ReportedAt >= month)
            .GroupBy(r => r.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        // Every severity is listed, so a chart never silently drops a category
        dto.SeverityBreakdown30d = Enum.GetValues<Severity>()
            .Select(s => new SeverityCountDto { Severity = s, Count = severities.FirstOrDefault(x => x.Severity == s)?.Count ?? 0 })
            .ToList();

        dto.TopCities30d = await visible
            .Where(r => r.ReportedAt >= month && r.City != null)
            .GroupBy(r => r.City!)
            .Select(g => new PlaceCountDto { Place = g.Key, Count = g.Count() })
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Place)
            .Take(8)
            .ToListAsync(cancellationToken);

        var perDay = await visible
            .Where(r => r.ReportedAt >= month.Date)
            .GroupBy(r => r.ReportedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var byDay = perDay.ToDictionary(d => DateOnly.FromDateTime(d.Day), d => d.Count);

        // A continuous series: days without reports are zeros, not gaps
        dto.DailyReports30d = Enumerable.Range(0, Days + 1)
            .Select(i => DateOnly.FromDateTime(month.Date.AddDays(i)))
            .Select(d => new DayCountDto { Day = d, Count = byDay.GetValueOrDefault(d) })
            .ToList();

        return dto;
    }
}
