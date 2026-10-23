using MasidBaha.Application.Common.Enums;
using NetTopologySuite.Geometries;

namespace MasidBaha.Application.Persistence;

public class FloodReport
{
    public Guid Id { get; set; }

    // geography(Point, 4326): X is longitude, Y is latitude (PostGIS order)
    public Point Location { get; set; } = null!;

    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ConfidenceScore { get; set; } = 1;
    public ReportStatus Status { get; set; } = ReportStatus.Active;
    public string ReporterSessionId { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }

    public List<ReportVote> Votes { get; set; } = new();
}

public class ReportVote
{
    public Guid Id { get; set; }
    public Guid FloodReportId { get; set; }
    public string VoterSessionId { get; set; } = string.Empty;
    public VoteType VoteType { get; set; }
    public DateTime VotedAt { get; set; }

    public FloodReport? FloodReport { get; set; }
}
