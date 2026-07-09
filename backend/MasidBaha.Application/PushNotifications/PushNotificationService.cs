using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace MasidBaha.Application.PushNotifications;

public interface IPushNotificationService
{
    // Sends to every stored subscription. Should never throw, since a push
    // failure shouldn't stop a report from being created.
    Task BroadcastAsync(PushPayload payload);
}

public class PushNotificationService : IPushNotificationService
{
    private readonly IPushSubscriptionService _subscriptionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IPushSubscriptionService subscriptionService,
        IConfiguration configuration,
        ILogger<PushNotificationService> logger)
    {
        _subscriptionService = subscriptionService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task BroadcastAsync(PushPayload payload)
    {
        var publicKey = _configuration["Vapid:PublicKey"];
        var privateKey = _configuration["Vapid:PrivateKey"];
        var subject = _configuration["Vapid:Subject"];

        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("Vapid keys are not configured, skipping push broadcast.");
            return;
        }

        var subscriptions = await _subscriptionService.GetAllAsync();
        if (subscriptions.Count == 0) return;

        var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
        var client = new WebPushClient();

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
            try
            {
                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await client.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogInformation("Push subscription expired, removing: {Endpoint}", sub.Endpoint);
                await _subscriptionService.UnsubscribeAsync(sub.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send push notification to {Endpoint}", sub.Endpoint);
            }
        }
    }
}
