using MasidBaha.Application.Common.Enums;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public static class VoteTypeExtensions
{
    // The signed amount a vote adds to a report's confidence score. Must stay a
    // signed type: casting the -1 of Deny to byte yields 255, which once made
    // every deny vote *raise* the score by 255.
    public static short ToDelta(this VoteType voteType) => (short)voteType;
}
