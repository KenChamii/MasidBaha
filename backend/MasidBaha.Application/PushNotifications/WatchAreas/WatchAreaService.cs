using FluentValidation;
using MasidBaha.Application.Common.Exceptions;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.PushNotifications.WatchAreas;

public class WatchAreaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RadiusMeters { get; set; }
}

public class CreateWatchAreaRequest
{
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RadiusMeters { get; set; } = 2000;
}

public class CreateWatchAreaRequestValidator : AbstractValidator<CreateWatchAreaRequest>
{
    public const int MinRadiusMeters = 500;
    public const int MaxRadiusMeters = 20_000;

    public CreateWatchAreaRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(40);

        // Same geofence as reports: an area outside the Philippines can never match anything
        RuleFor(x => x.Lat)
            .InclusiveBetween(PhilippinesBounds.MinLat, PhilippinesBounds.MaxLat)
            .WithMessage("Watch areas must be located within the Philippines.");
        RuleFor(x => x.Lng)
            .InclusiveBetween(PhilippinesBounds.MinLng, PhilippinesBounds.MaxLng)
            .WithMessage("Watch areas must be located within the Philippines.");

        RuleFor(x => x.RadiusMeters).InclusiveBetween(MinRadiusMeters, MaxRadiusMeters);
    }
}

public interface IWatchAreaService
{
    Task<List<WatchAreaDto>> ListAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<WatchAreaDto> CreateAsync(string sessionId, CreateWatchAreaRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string sessionId, Guid id, CancellationToken cancellationToken = default);
}

public class WatchAreaService : IWatchAreaService
{
    public const int MaxPerVisitor = 5;

    private readonly MasidBahaDbContext _db;

    public WatchAreaService(MasidBahaDbContext db) => _db = db;

    public async Task<List<WatchAreaDto>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var areas = await _db.WatchAreas.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return areas.Select(ToDto).ToList();
    }

    public async Task<WatchAreaDto> CreateAsync(string sessionId, CreateWatchAreaRequest request, CancellationToken cancellationToken = default)
    {
        // Areas only make sense next to a push subscription, and this stops a stream of
        // throwaway sessions from filling the table with areas nobody can be alerted about
        if (!await _db.PushSubscriptions.AnyAsync(s => s.SessionId == sessionId, cancellationToken))
            throw new ConflictException("Turn on alerts first, then choose the places you want to watch.");

        if (await _db.WatchAreas.CountAsync(a => a.SessionId == sessionId, cancellationToken) >= MaxPerVisitor)
            throw new ConflictException($"You can watch up to {MaxPerVisitor} places. Remove one first.");

        var area = new WatchArea
        {
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            Name = request.Name.Trim(),
            Location = FloodReportMappings.ToPoint(request.Lat, request.Lng),
            RadiusMeters = request.RadiusMeters,
            CreatedAt = DateTime.UtcNow
        };

        _db.WatchAreas.Add(area);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(area);
    }

    // Scoped to the session, so one visitor can never delete another's area by guessing an id
    public async Task<bool> DeleteAsync(string sessionId, Guid id, CancellationToken cancellationToken = default) =>
        await _db.WatchAreas.Where(a => a.Id == id && a.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken) > 0;

    private static WatchAreaDto ToDto(WatchArea a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        Lat = a.Location.Y,
        Lng = a.Location.X,
        RadiusMeters = a.RadiusMeters
    };
}
