namespace MasidBaha.Application.FloodReports.GetNearbyReports;

public class NearbyReportsQuery
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RadiusMeters { get; set; } = 5000;
}