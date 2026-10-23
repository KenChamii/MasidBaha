using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Persistence;

namespace MasidBaha.Tests;

// The voting rule lives on the entity, so it is tested without any database
public class FloodReportTests
{
    private static FloodReport NewReport(int score = 1) => new()
    {
        ConfidenceScore = score,
        Status = ReportStatus.Active,
        ExpiresAt = DateTime.UtcNow.AddHours(6)
    };

    [Fact]
    public void Confirm_RaisesScoreAndKeepsReportActive()
    {
        var report = NewReport();

        report.ApplyVote(VoteType.Confirm);

        Assert.Equal(2, report.ConfidenceScore);
        Assert.Equal(ReportStatus.Active, report.Status);
    }

    [Fact]
    public void Deny_OnAFreshReport_ResolvesIt()
    {
        // Regression for the (byte)-1 == 255 bug: a deny must LOWER the score
        var report = NewReport(1);

        report.ApplyVote(VoteType.Deny);

        Assert.Equal(0, report.ConfidenceScore);
        Assert.Equal(ReportStatus.Resolved, report.Status);
    }

    [Fact]
    public void Deny_OnAWellConfirmedReport_OnlyLowersTheScore()
    {
        var report = NewReport(5);

        report.ApplyVote(VoteType.Deny);

        Assert.Equal(4, report.ConfidenceScore);
        Assert.Equal(ReportStatus.Active, report.Status);
    }

    [Fact]
    public void ScoreCanReachZeroOnlyThroughRepeatedDenies()
    {
        var report = NewReport(3);

        report.ApplyVote(VoteType.Deny);
        report.ApplyVote(VoteType.Deny);
        Assert.Equal(ReportStatus.Active, report.Status);

        report.ApplyVote(VoteType.Deny);
        Assert.Equal(ReportStatus.Resolved, report.Status);
    }

    [Fact]
    public void IsOpen_RequiresActiveStatusAndFutureExpiry()
    {
        var now = DateTime.UtcNow;

        Assert.True(NewReport().IsOpen(now));

        var expired = NewReport();
        expired.ExpiresAt = now.AddMinutes(-1); // still Active until the sweeper runs
        Assert.False(expired.IsOpen(now));

        var resolved = NewReport();
        resolved.Status = ReportStatus.Resolved;
        Assert.False(resolved.IsOpen(now));
    }
}
