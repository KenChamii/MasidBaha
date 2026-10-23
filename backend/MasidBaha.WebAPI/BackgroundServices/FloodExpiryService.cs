using MasidBaha.Application.Common.Options;
using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.WebAPI.Controllers;
using MasidBaha.WebAPI.Hubs;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.BackgroundServices;

public class FloodExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<FloodHub> _hubContext;
    private readonly IOutputCacheStore _cache;
    private readonly ILogger<FloodExpiryService> _logger;
    private readonly ExpiryOptions _options;

    public FloodExpiryService(
        IServiceScopeFactory scopeFactory,
        IHubContext<FloodHub> hubContext,
        IOutputCacheStore cache,
        ILogger<FloodExpiryService> logger,
        IOptions<ExpiryOptions> options)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
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
                {
                    await _cache.EvictByTagAsync(FloodReportsController.CacheTag, stoppingToken);
                    _logger.LogInformation("Expired {Count} flood reports", expiredIds.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flood report expiry sweep");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.SweepIntervalMinutes), stoppingToken);
        }
    }
}