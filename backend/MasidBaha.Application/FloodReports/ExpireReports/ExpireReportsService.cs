using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.FloodReports.ExpireReports;

public interface IExpireReportsService
{
    Task<List<Guid>> ExpireStaleReportsAsync();
}

// Reads already hide expired reports (see the ExpiresAt filters), so this sweep
// only flips the stored status and lets connected clients know.
public class ExpireReportsService : IExpireReportsService
{
    private readonly MasidBahaDbContext _db;

    public ExpireReportsService(MasidBahaDbContext db)
    {
        _db = db;
    }

    public async Task<List<Guid>> ExpireStaleReportsAsync()
    {
        // One statement: update and return the affected ids atomically
        return await _db.Database
            .SqlQuery<Guid>($"UPDATE flood_reports SET status = 3 WHERE status = 1 AND expires_at <= now() RETURNING id AS \"Value\"")
            .ToListAsync();
    }
}
