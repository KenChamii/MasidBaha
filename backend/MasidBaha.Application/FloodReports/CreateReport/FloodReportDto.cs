using System.ComponentModel.DataAnnotations;
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
    [Range(-90, 90, ErrorMessage = "Lat must be between -90 and 90.")]
    public double Lat { get; set; }

    [Range(-180, 180, ErrorMessage = "Lng must be between -180 and 180.")]
    public double Lng { get; set; }

    [EnumDataType(typeof(Severity), ErrorMessage = "Invalid severity value.")]
    public Severity Severity { get; set; }

    [MaxLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    [MaxLength(500)]
    [Url(ErrorMessage = "PhotoUrl must be a valid URL.")]
    public string? PhotoUrl { get; set; }

    [Required(ErrorMessage = "ReporterSessionId is required.")]
    [MaxLength(100)]
    public string ReporterSessionId { get; set; } = string.Empty;
}