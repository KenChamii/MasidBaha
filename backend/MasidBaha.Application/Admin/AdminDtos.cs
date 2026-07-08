using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.Admin;

// Fuller than the public FloodReportDto on purpose — admins reviewing for
// spam/fake reports need ReporterSessionId (to spot one session flooding
// the map with junk pins) and ExpiresAt, neither of which the public API
// exposes.
public class AdminFloodReportDto
{
    public Guid Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
    public string ReporterSessionId { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}

public class AdminGetReportsQuery
{
    public ReportStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class AdminGetReportsResult
{
    public List<AdminFloodReportDto> Reports { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AdminSetStatusRequest
{
    public ReportStatus Status { get; set; }
}

public class AdminStatusResult
{
    public ReportStatus Status { get; set; }
    public int ConfidenceScore { get; set; }
}
