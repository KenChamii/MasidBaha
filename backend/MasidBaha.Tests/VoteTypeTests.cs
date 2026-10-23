using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.FloodReports.VoteOnReport;

namespace MasidBaha.Tests;

public class VoteTypeTests
{
    [Fact]
    public void Deny_IsNegativeOne_NotWrappedToByte()
    {
        // Regression: (byte)VoteType.Deny is 255, which once made every deny
        // vote add 255 to a report's confidence score.
        Assert.Equal(-1, VoteType.Deny.ToDelta());
        Assert.NotEqual(255, VoteType.Deny.ToDelta());
    }

    [Fact]
    public void Confirm_IsPositiveOne()
    {
        Assert.Equal(1, VoteType.Confirm.ToDelta());
    }

    [Theory]
    [InlineData(VoteType.Deny)]
    [InlineData(VoteType.Confirm)]
    public void Delta_IsAlwaysASingleStep(VoteType voteType)
    {
        Assert.InRange(Math.Abs((int)voteType.ToDelta()), 1, 1);
    }
}
