using FluentValidation.TestHelper;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.PushNotifications;

namespace MasidBaha.Tests;

public class ValidatorTests
{
    private static CreateFloodReportRequest ValidReport() => new()
    {
        Lat = 14.65, Lng = 121.10, Severity = Severity.KneeLevel, Notes = "Water is rising"
    };

    private readonly CreateFloodReportRequestValidator _create = new();

    [Fact]
    public void Report_InsideThePhilippines_IsValid()
    {
        _create.TestValidate(ValidReport()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(35.68, 139.69)]   // Tokyo
    [InlineData(1.35, 103.82)]    // Singapore
    [InlineData(0, 0)]            // null island
    [InlineData(14.65, 100.5)]    // right latitude, wrong longitude
    public void Report_OutsideThePhilippines_IsRejected(double lat, double lng)
    {
        var result = _create.TestValidate(new CreateFloodReportRequest { Lat = lat, Lng = lng, Severity = Severity.Passable });

        Assert.True(result.Errors.Count > 0);
        Assert.All(result.Errors, e => Assert.Contains("Philippines", e.ErrorMessage));
    }

    [Theory]
    [InlineData(5.5, 120.0)]      // southern Mindanao / Sulu
    [InlineData(20.5, 121.9)]     // Batanes
    [InlineData(10.3, 123.9)]     // Cebu
    public void Report_AtTheCountrysExtremes_IsValid(double lat, double lng)
    {
        _create.TestValidate(new CreateFloodReportRequest { Lat = lat, Lng = lng, Severity = Severity.Passable })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Report_RejectsBadSeverityAndLongNotes()
    {
        var request = ValidReport();
        request.Severity = (Severity)9;
        request.Notes = new string('x', 501);

        var result = _create.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Severity);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Vote_RejectsUndefinedVoteTypes()
    {
        var validator = new VoteRequestValidator();

        validator.TestValidate(new VoteRequest { VoteType = VoteType.Confirm }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new VoteRequest { VoteType = VoteType.Deny }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new VoteRequest { VoteType = (VoteType)0 }).ShouldHaveValidationErrorFor(x => x.VoteType);
        validator.TestValidate(new VoteRequest { VoteType = (VoteType)255 }).ShouldHaveValidationErrorFor(x => x.VoteType);
    }

    [Fact]
    public void Nearby_AcceptsAnyRealCoordinate_ButBoundsTheRadius()
    {
        var validator = new NearbyReportsQueryValidator();

        // A viewer outside the Philippines can still look at the map
        validator.TestValidate(new NearbyReportsQuery { Lat = 35.68, Lng = 139.69, RadiusMeters = 5000 })
            .ShouldNotHaveAnyValidationErrors();

        validator.TestValidate(new NearbyReportsQuery { Lat = 95, Lng = 0, RadiusMeters = 5000 }).ShouldHaveValidationErrorFor(x => x.Lat);
        validator.TestValidate(new NearbyReportsQuery { Lat = 0, Lng = 190, RadiusMeters = 5000 }).ShouldHaveValidationErrorFor(x => x.Lng);
        validator.TestValidate(new NearbyReportsQuery { Lat = 0, Lng = 0, RadiusMeters = 0 }).ShouldHaveValidationErrorFor(x => x.RadiusMeters);
        validator.TestValidate(new NearbyReportsQuery { Lat = 0, Lng = 0, RadiusMeters = 1_000_000 }).ShouldHaveValidationErrorFor(x => x.RadiusMeters);
    }

    [Fact]
    public void Top_RequiresAPositiveLimit()
    {
        var validator = new TopReportsQueryValidator();

        validator.TestValidate(new TopReportsQuery { Limit = 20 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new TopReportsQuery { Limit = 0 }).ShouldHaveValidationErrorFor(x => x.Limit);
        validator.TestValidate(new TopReportsQuery { Limit = -5 }).ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public void Heatmap_RejectsAReversedDateRange()
    {
        var validator = new HeatmapQueryValidator();
        var now = DateTime.UtcNow;

        validator.TestValidate(new HeatmapQuery { FromDate = now.AddDays(-5), ToDate = now }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new HeatmapQuery { FromDate = now, ToDate = now.AddDays(-5) }).ShouldHaveValidationErrorFor(x => x.ToDate);
        validator.TestValidate(new HeatmapQuery { ToDate = now }).ShouldNotHaveAnyValidationErrors(); // open-ended is fine
    }

    [Fact]
    public void AdminListing_BoundsPagingAndStatus()
    {
        var validator = new AdminGetReportsQueryValidator();

        validator.TestValidate(new AdminGetReportsQuery { Page = 1, PageSize = 25 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new AdminGetReportsQuery { Page = 0 }).ShouldHaveValidationErrorFor(x => x.Page);
        validator.TestValidate(new AdminGetReportsQuery { PageSize = 101 }).ShouldHaveValidationErrorFor(x => x.PageSize);
        validator.TestValidate(new AdminGetReportsQuery { Status = (ReportStatus)7 }).ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void PushSubscription_RequiresAnHttpsEndpointAndKeys()
    {
        var validator = new PushSubscriptionDtoValidator();
        var valid = new PushSubscriptionDto
        {
            Endpoint = "https://fcm.googleapis.com/fcm/send/abc",
            Keys = new PushSubscriptionKeysDto { P256dh = "key", Auth = "auth" }
        };

        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        valid.Endpoint = "http://insecure.example.com/push";
        validator.TestValidate(valid).ShouldHaveValidationErrorFor(x => x.Endpoint);

        valid.Endpoint = "not a url";
        validator.TestValidate(valid).ShouldHaveValidationErrorFor(x => x.Endpoint);

        valid.Endpoint = "https://fcm.googleapis.com/fcm/send/abc";
        valid.Keys.P256dh = "";
        validator.TestValidate(valid).ShouldHaveValidationErrorFor("Keys.P256dh");
    }
}
