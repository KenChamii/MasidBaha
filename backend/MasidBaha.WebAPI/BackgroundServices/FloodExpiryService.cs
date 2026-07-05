using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MasidBaha.WebAPI.BackgroundServices;

public class FloodExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<FloodHub> _hubContext;
    private readonly ILogger<FloodExpiryService> _logger;

    public FloodExpiryService(
        IServiceScopeFactory scopeFactory,
        IHubContext<FloodHub> hubContext,
        ILogger<FloodExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var expireService = scope.ServiceProvider.GetRequiredService<IExpireReportsService>();
                var expiredIds = await expireService.ExpireStaleReportsAsync();

                foreach (var id in expiredIds)
                    await _hubContext.Clients.All.SendAsync("RemoveReport", id, stoppingToken);

                if (expiredIds.Count > 0)
                    _logger.LogInformation("Expired {Count} flood reports", expiredIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flood report expiry sweep");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}