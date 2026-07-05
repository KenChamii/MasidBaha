using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.CreateReport;

public class FloodReportDto
{
    public Guid Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime ReportedAt { get; set; }
    public int ConfidenceScore { get; set; }
    public ReportStatus Status { get; set; }
}

public class CreateFloodReportRequest
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public string ReporterSessionId { get; set; } = string.Empty;
}