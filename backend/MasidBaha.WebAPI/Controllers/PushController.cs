using MasidBaha.Application.PushNotifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MasidBaha.WebAPI.Controllers;

[ApiController]
[Route("api/push")]
public class PushController : ControllerBase
{
    private readonly IPushSubscriptionService _subscriptionService;
    private readonly IConfiguration _configuration;

    public PushController(IPushSubscriptionService subscriptionService, IConfiguration configuration)
    {
        _subscriptionService = subscriptionService;
        _configuration = configuration;
    }

    // Frontend fetches the VAPID public key at runtime instead of hardcoding
    // it in environment.ts \u2014 one less place to keep in sync if the key
    // ever gets rotated.
    [HttpGet("vapid-public-key")]
    public ActionResult<object> GetVapidPublicKey()
    {
        var publicKey = _configuration["Vapid:PublicKey"];
        if (string.IsNullOrWhiteSpace(publicKey))
            return NotFound(new { error = "Push notifications are not configured on this server." });

        return Ok(new { publicKey });
    }

    [HttpPost("subscribe")]
    [EnableRateLimiting("push-writes")]
    public async Task<ActionResult> Subscribe(PushSubscriptionDto subscription)
    {
        await _subscriptionService.SubscribeAsync(subscription);
        return NoContent();
    }

    [HttpPost("unsubscribe")]
    [EnableRateLimiting("push-writes")]
    public async Task<ActionResult> Unsubscribe(UnsubscribeRequest request)
    {
        await _subscriptionService.UnsubscribeAsync(request.Endpoint);
        return NoContent();
    }
}
