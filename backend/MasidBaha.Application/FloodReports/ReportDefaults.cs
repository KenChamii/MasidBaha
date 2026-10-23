namespace MasidBaha.Application.FloodReports;

public static class ReportDefaults
{
    // How long a report stays visible without being refreshed
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    public const int MaxTopReports = 200;
}
