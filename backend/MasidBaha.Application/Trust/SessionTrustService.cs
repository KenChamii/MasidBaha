using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.Trust;

public class SessionTrustDto
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalReports { get; set; }
    public int ResolvedReports { get; set; }
    public int ActiveReports { get; set; }
    public double AvgConfidenceScore { get; set; }
    public DateTime? FirstReportAt { get; set; }
    public DateTime? LastReportAt { get; set; }

    // 0-100, or null for a session with no report history yet ("Unrated"
    // rather than assuming either trustworthy or suspicious by default).
    // Deliberately simple and explainable for an admin glancing at it: what
    // share of this session's reports the community ended up confirming
    // (Status = Resolved) rather than letting expire untouched.
    public int? TrustScore =>
        TotalReports == 0 ? null : (int)Math.Round((double)ResolvedReports / TotalReports * 100);
}

public interface ISessionTrustService
{
    Task<SessionTrustDto> GetTrustScoreAsync(string sessionId);
}

public class SessionTrustService : ISessionTrustService
{
    private readonly MasidBahaDbContext _db;

    public SessionTrustService(MasidBahaDbContext db)
    {
        _db = db;
    }

    // Only looks at reports made by this session, not votes it cast on others.
    public async Task<SessionTrustDto> GetTrustScoreAsync(string sessionId)
    {
        var stats = await _db.FloodReports
            .AsNoTracking()
            .Where(r => r.ReporterSessionId == sessionId)
            .GroupBy(r => r.ReporterSessionId)
            .Select(g => new
            {
                Total = g.Count(),
                Resolved = g.Count(r => r.Status == ReportStatus.Resolved),
                Active = g.Count(r => r.Status == ReportStatus.Active),
                AvgConfidence = g.Average(r => (double)r.ConfidenceScore),
                First = g.Min(r => r.ReportedAt),
                Last = g.Max(r => r.ReportedAt)
            })
            .FirstOrDefaultAsync();

        if (stats is null)
            return new SessionTrustDto { SessionId = sessionId };

        return new SessionTrustDto
        {
            SessionId = sessionId,
            TotalReports = stats.Total,
            ResolvedReports = stats.Resolved,
            ActiveReports = stats.Active,
            AvgConfidenceScore = stats.AvgConfidence,
            FirstReportAt = stats.First,
            LastReportAt = stats.Last
        };
    }
}
