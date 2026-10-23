using FluentValidation;

namespace MasidBaha.Application.FloodReports.GetNearbyReports;

public class NearbyReportsQuery
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RadiusMeters { get; set; } = 5000;
}
public class NearbyReportsQueryValidator : AbstractValidator<NearbyReportsQuery>
{
    public const int MaxRadiusMeters = 50_000;

    public NearbyReportsQueryValidator()
    {
        // Any valid coordinate is fine here (a viewer may be outside the Philippines);
        // only reports themselves are geofenced.
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusMeters).InclusiveBetween(1, MaxRadiusMeters);
    }
}
