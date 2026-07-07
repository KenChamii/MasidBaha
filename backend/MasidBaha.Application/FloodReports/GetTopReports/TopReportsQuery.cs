namespace MasidBaha.Application.FloodReports.GetTopReports;

public class TopReportsQuery
{
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public int Limit { get; set; } = 20;
}
