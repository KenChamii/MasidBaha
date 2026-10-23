using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Weather;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.BackgroundServices;

// Keeps the typhoon data fresh without any visitor waiting for the (slow) source
public class TyphoonRefreshService : BackgroundService
{
    // After a failure, try again sooner than the normal schedule
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(3);

    private readonly ITyphoonService _typhoons;
    private readonly WeatherOptions _options;
    private readonly ILogger<TyphoonRefreshService> _logger;

    public TyphoonRefreshService(ITyphoonService typhoons, IOptions<WeatherOptions> options, ILogger<TyphoonRefreshService> logger)
    {
        _typhoons = typhoons;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Whatever was saved last time is served straight away, while the slow download runs
        try
        {
            await _typhoons.LoadSavedAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not load the saved typhoon data");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromMinutes(_options.CacheMinutes);

            try
            {
                await _typhoons.RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not refresh typhoon data; keeping the previous copy");
                wait = RetryAfterFailure;
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
