using FluentValidation;

namespace MasidBaha.Application.FloodReports.GetTopReports;

public class TopReportsQuery
{
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public int Limit { get; set; } = 20;
}

public class TopReportsQueryValidator : AbstractValidator<TopReportsQuery>
{
    public TopReportsQueryValidator()
    {
        RuleFor(x => x.Limit).GreaterThan(0);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.Province).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
    }
}
