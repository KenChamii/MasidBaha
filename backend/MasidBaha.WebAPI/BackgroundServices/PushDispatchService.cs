using MasidBaha.Application.PushNotifications;

namespace MasidBaha.WebAPI.BackgroundServices;

// Drains the push queue and sends each notification outside any HTTP request
public class PushDispatchService : BackgroundService
{
    private readonly IPushQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushDispatchService> _logger;

    public PushDispatchService(IPushQueue queue, IServiceScopeFactory scopeFactory, ILogger<PushDispatchService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
                await push.BroadcastAsync(payload, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Push dispatch failed");
            }
        }
    }
}
