using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.GetHeatmapData;

// Deliberately thin — a heatmap only needs position + a weighting signal
// (severity) plus enough metadata for the frontend to filter/bucket by
// time or status without a second round trip.
public class HeatmapPointDto
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Severity Severity { get; set; }
    public ReportStatus Status { get; set; }
    public DateTime ReportedAt { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}
