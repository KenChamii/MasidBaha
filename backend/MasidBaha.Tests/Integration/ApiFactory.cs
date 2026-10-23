using System.Runtime.CompilerServices;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.PushNotifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MasidBaha.Tests.Integration;

// Records pushes instead of sending them
public class RecordingPushQueue : IPushQueue
{
    public List<PushPayload> Enqueued { get; } = new();

    public ValueTask EnqueueAsync(PushPayload payload)
    {
        lock (Enqueued) Enqueued.Add(payload);
        return ValueTask.CompletedTask;
    }

    // The dispatcher just waits; nothing is ever delivered
    public async IAsyncEnumerable<PushPayload> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }
}

public class FakeGeocoder : IGeocodingService
{
    public Task<GeocodeResult> ReverseGeocodeAsync(double lat, double lng) =>
        Task.FromResult(new GeocodeResult { Region = "NCR", Province = "Metro Manila", City = "Marikina" });
}

// The real application, hosted in-process against the test PostGIS container.
// The only substitutes are the outside world: geocoding, push delivery, photo disk.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";

    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private readonly string _uploadsDir = Path.Combine(Path.GetTempPath(), "masidbaha-tests-" + Guid.NewGuid().ToString("N"));

    public RecordingPushQueue Push { get; } = new();

    private readonly bool _fakeStorage;

    public ApiFactory(string connectionString, Dictionary<string, string?>? overrides = null, string environment = "Testing", bool fakeStorage = true)
    {
        _environment = environment;
        _fakeStorage = fakeStorage;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MasidBahaDb"] = connectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedDemoData"] = "false",
            ["Admin:ApiKey"] = AdminKey,
            ["Security:SessionSigningKey"] = new string('k', 40),
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            ["RateLimits:ReportWrites:PermitLimit"] = "1000",
            ["RateLimits:VoteWrites:PermitLimit"] = "1000",
            ["RateLimits:PushWrites:PermitLimit"] = "1000",
            ["Serilog:MinimumLevel:Default"] = "Warning"
        };

        if (overrides is not null)
            foreach (var (key, value) in overrides)
                _settings[key] = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        // UseSetting (not ConfigureAppConfiguration): Program reads configuration while
        // registering services, before late-added configuration sources would apply
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGeocodingService>();
            services.AddSingleton<IGeocodingService, FakeGeocoder>();

            services.RemoveAll<IPushQueue>();
            services.AddSingleton<IPushQueue>(Push);

            if (!_fakeStorage) return;

            // Keep uploaded test photos out of the source tree
            services.RemoveAll<IPhotoStorageService>();
            services.AddSingleton<IPhotoStorageService>(new LocalDiskPhotoStorageService(_uploadsDir));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_uploadsDir))
            Directory.Delete(_uploadsDir, recursive: true);
    }
}
