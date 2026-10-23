using MasidBaha.Application.Common.Enums;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MasidBaha.Application.FloodReports.VoteOnReport;

public interface IVoteOnReportService
{
    Task<VoteResultDto> VoteAsync(Guid floodReportId, VoteRequest request);
}

public class VoteOnReportService : IVoteOnReportService
{
    private readonly MasidBahaDbContext _db;

    public VoteOnReportService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<VoteResultDto> VoteAsync(Guid floodReportId, VoteRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        // FOR UPDATE locks the report row so concurrent votes on it are serialized
        var report = await _db.FloodReports
            .FromSqlInterpolated($"SELECT * FROM flood_reports WHERE id = {floodReportId} FOR UPDATE")
            .SingleOrDefaultAsync();

        if (report is null)
            throw new NotFoundException("Report not found.");

        if (report.Status != ReportStatus.Active || report.ExpiresAt <= DateTime.UtcNow)
            throw new ConflictException("This report is no longer active.");

        var alreadyVoted = await _db.ReportVotes.AnyAsync(v =>
            v.FloodReportId == floodReportId && v.VoterSessionId == request.VoterSessionId);
        if (alreadyVoted)
            throw new ConflictException("You have already voted on this report.");

        _db.ReportVotes.Add(new ReportVote
        {
            Id = Guid.CreateVersion7(),
            FloodReportId = floodReportId,
            VoterSessionId = request.VoterSessionId,
            VoteType = request.VoteType,
            VotedAt = DateTime.UtcNow
        });

        report.ConfidenceScore += request.VoteType.ToDelta();
        if (report.ConfidenceScore <= 0)
            report.Status = ReportStatus.Resolved;

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Backstop for a race the row lock didn't cover
            throw new ConflictException("You have already voted on this report.");
        }

        return new VoteResultDto
        {
            FloodReportId = floodReportId,
            ConfidenceScore = report.ConfidenceScore,
            Status = report.Status
        };
    }
}
