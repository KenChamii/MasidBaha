using System.Text.Json;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace MasidBaha.Application.PushNotifications;

public interface IPushNotificationService
{
    // Sends to the subscriptions that should hear about it (see PushPayload). Should never throw, since a push
    // failure shouldn't stop a report from being created.
    Task BroadcastAsync(PushPayload payload, CancellationToken cancellationToken = default);
}

public class PushNotificationService : IPushNotificationService
{
    private readonly IPushSubscriptionService _subscriptionService;
    private readonly WebPushClient _client;
    private readonly VapidOptions _vapid;
    private readonly ILogger<PushNotificationService> _logger;

    // WebPushClient is injected (one shared HttpClient pipeline) instead of
    // being created for every broadcast.
    public PushNotificationService(
        IPushSubscriptionService subscriptionService,
        WebPushClient client,
        IOptions<VapidOptions> vapid,
        ILogger<PushNotificationService> logger)
    {
        _subscriptionService = subscriptionService;
        _client = client;
        _vapid = vapid.Value;
        _logger = logger;
    }

    public async Task BroadcastAsync(PushPayload payload, CancellationToken cancellationToken = default)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogWarning("Vapid keys are not configured, skipping push broadcast.");
            return;
        }

        var subscriptions = await _subscriptionService.GetRecipientsAsync(payload.Lat, payload.Lng, payload.RadiusMeters, cancellationToken);
        if (subscriptions.Count == 0) return;

        var vapidDetails = new VapidDetails(_vapid.Subject, _vapid.PublicKey, _vapid.PrivateKey);

        // Angular's built-in service worker can display these on its own as
        // long as the payload has this shape. That means we don't need a
        // custom service worker on the frontend for this to work.
        var payloadJson = JsonSerializer.Serialize(new
        {
            notification = new
            {
                title = payload.Title,
                body = payload.Body,
                icon = "/favicon.ico",
                vibrate = new[] { 200, 100, 200 },
                data = new
                {
                    onActionClick = new
                    {
                        // default is a reserved word in C#, so it needs the @ prefix.
                        // It still serializes as a normal "default" key in the JSON.
                        @default = new { operation = "openWindow", url = payload.Url }
                    }
                }
            }
        });

        // Send one at a time so a single dead subscription doesn't stop the
        // rest of the broadcast. Clean up subscriptions the push service
        // tells us are expired.
        foreach (var sub in subscriptions)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await _client.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails, cancellationToken);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogInformation("Push subscription expired, removing: {Endpoint}", sub.Endpoint);
                await _subscriptionService.UnsubscribeAsync(sub.Endpoint);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send push notification to {Endpoint}", sub.Endpoint);
            }
        }
    }
}
