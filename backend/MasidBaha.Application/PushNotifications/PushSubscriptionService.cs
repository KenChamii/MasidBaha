using MasidBaha.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasidBaha.Application.PushNotifications;

public interface IPushSubscriptionService
{
    Task SubscribeAsync(PushSubscriptionDto subscription);
    Task UnsubscribeAsync(string endpoint);
    Task<List<StoredPushSubscription>> GetAllAsync();
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
        await _db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync();
    }

    public async Task<List<StoredPushSubscription>> GetAllAsync()
    {
        return await _db.PushSubscriptions.AsNoTracking().ToListAsync();
    }
}
