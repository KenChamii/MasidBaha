namespace MasidBaha.Application.FloodReports.GetHeatmapData;

public class HeatmapQuery
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}
