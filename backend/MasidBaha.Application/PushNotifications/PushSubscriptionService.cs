using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.PushNotifications;

public interface IPushSubscriptionService
{
    Task SubscribeAsync(PushSubscriptionDto subscription);
    Task UnsubscribeAsync(string endpoint);
    Task<List<StoredPushSubscription>> GetAllAsync();

    // Who should hear about something at this place (see PushPayload). No location means everyone.
    Task<List<StoredPushSubscription>> GetRecipientsAsync(double? lat, double? lng, int? radiusMeters, CancellationToken cancellationToken = default);
    Task<int> CountRecipientsAsync(double? lat, double? lng, int? radiusMeters, CancellationToken cancellationToken = default);
}

public class StoredPushSubscription
{
    public Guid Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PushSubscriptionService : IPushSubscriptionService
{
    private readonly MasidBahaDbContext _db;

    public PushSubscriptionService(MasidBahaDbContext db)
    {
        _db = db;
    }

    // Upserts by endpoint so re-subscribing just updates the existing row
    // instead of creating a duplicate. ON CONFLICT keeps it atomic.
    public async Task SubscribeAsync(PushSubscriptionDto subscription)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO push_subscriptions (id, session_id, endpoint, p256dh, auth, created_at)
            VALUES ({Guid.CreateVersion7()}, {subscription.SessionId}, {subscription.Endpoint},
                    {subscription.Keys.P256dh}, {subscription.Keys.Auth}, now())
            ON CONFLICT (endpoint) DO UPDATE
            SET session_id = EXCLUDED.session_id, p256dh = EXCLUDED.p256dh, auth = EXCLUDED.auth");
    }

    public async Task UnsubscribeAsync(string endpoint)
    {
        var sessionId = await _db.PushSubscriptions.Where(s => s.Endpoint == endpoint).Select(s => s.SessionId).FirstOrDefaultAsync();
        await _db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync();

        // Watch areas are only useful with a subscription behind them: drop them with the last one
        if (sessionId is not null && !await _db.PushSubscriptions.AnyAsync(s => s.SessionId == sessionId))
            await _db.WatchAreas.Where(a => a.SessionId == sessionId).ExecuteDeleteAsync();
    }

    public async Task<List<StoredPushSubscription>> GetAllAsync()
    {
        return await _db.PushSubscriptions.AsNoTracking().ToListAsync();
    }

    public Task<List<StoredPushSubscription>> GetRecipientsAsync(double? lat, double? lng, int? radiusMeters, CancellationToken cancellationToken = default) =>
        RecipientsQuery(lat, lng, radiusMeters).ToListAsync(cancellationToken);

    public Task<int> CountRecipientsAsync(double? lat, double? lng, int? radiusMeters, CancellationToken cancellationToken = default) =>
        RecipientsQuery(lat, lng, radiusMeters).CountAsync(cancellationToken);

    // ST_DWithin against the watch areas runs in PostgreSQL, on the GiST index
    private IQueryable<StoredPushSubscription> RecipientsQuery(double? lat, double? lng, int? radiusMeters)
    {
        var subscriptions = _db.PushSubscriptions.AsNoTracking();
        if (lat is null || lng is null) return subscriptions;

        var point = FloodReportMappings.ToPoint(lat.Value, lng.Value);

        if (radiusMeters is { } announcementRadius)
        {
            // An announcement for a circle: only people whose watched place overlaps it
            return subscriptions.Where(s => _db.WatchAreas.Any(a =>
                a.SessionId == s.SessionId && a.Location.IsWithinDistance(point, a.RadiusMeters + announcementRadius)));
        }

        // One event at one spot: people watching that spot, plus anyone who never chose places
        // (they keep getting everything, as before watch areas existed)
        return subscriptions.Where(s =>
            !_db.WatchAreas.Any(a => a.SessionId == s.SessionId) ||
            _db.WatchAreas.Any(a => a.SessionId == s.SessionId && a.Location.IsWithinDistance(point, a.RadiusMeters)));
    }
}
