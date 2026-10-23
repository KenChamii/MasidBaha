using MasidBaha.Application.Admin.Settings;

namespace MasidBaha.WebAPI.BackgroundServices;

// Keeps every API instance's in-memory settings current with the database
public class SettingsRefreshService : BackgroundService
{
    private readonly IRuntimeSettings _settings;
    private readonly ILogger<SettingsRefreshService> _logger;

    public SettingsRefreshService(IRuntimeSettings settings, ILogger<SettingsRefreshService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await _settings.ReloadAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not refresh runtime settings; keeping the current values");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
