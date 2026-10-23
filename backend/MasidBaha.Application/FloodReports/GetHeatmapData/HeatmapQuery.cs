using FluentValidation;

namespace MasidBaha.Application.FloodReports.GetHeatmapData;

public class HeatmapQuery
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
}

public class HeatmapQueryValidator : AbstractValidator<HeatmapQuery>
{
    public HeatmapQueryValidator()
    {
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue)
            .WithMessage("ToDate must not be earlier than FromDate.");

        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.Province).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
    }
}
