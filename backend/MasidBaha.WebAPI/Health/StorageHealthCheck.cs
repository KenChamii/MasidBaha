using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Common.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Health;

// Readiness check for photo storage: the uploads folder is writable (local)
// or the bucket answers (S3), so a broken storage setup shows up in /health/ready.
public class StorageHealthCheck : IHealthCheck
{
    private readonly IOptions<StorageOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IPhotoStorageService _storage;

    public StorageHealthCheck(IOptions<StorageOptions> options, IWebHostEnvironment environment, IPhotoStorageService storage)
    {
        _options = options;
        _environment = environment;
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_storage is S3PhotoStorageService s3)
            {
                // Listing one key proves credentials, bucket and network in one go
                await s3.CheckAvailabilityAsync(cancellationToken);
                return HealthCheckResult.Healthy("S3 storage reachable");
            }

            var root = Path.Combine(_environment.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [0], cancellationToken);
            File.Delete(probe);
            return HealthCheckResult.Healthy("Local storage writable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{_options.Value.Provider} storage unavailable", ex);
        }
    }
}
