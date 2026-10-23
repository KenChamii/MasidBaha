using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.PushNotifications;
using MasidBaha.Application.Weather;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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

// Stands in for GDACS: no storms, and no network
public class EmptyTyphoonSource : ITyphoonSource
{
    public Task<string> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult("{\"features\":[]}");
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
            ["Security:SessionSigningKey"] = new string('k', 40),
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            ["RateLimits:ReportWrites:PermitLimit"] = "1000",
            ["RateLimits:VoteWrites:PermitLimit"] = "1000",
            ["RateLimits:PushWrites:PermitLimit"] = "1000",
            ["RateLimits:AuthAttempts:PermitLimit"] = "1000",
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
            services.RemoveAll<ITyphoonSource>();
            services.AddSingleton<ITyphoonSource, EmptyTyphoonSource>();

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

    public const string StaffPassword = "correct-horse-battery-staple";

    // Creates a staff account with the given role (the roles are seeded at startup)
    public async Task<string> CreateStaffAsync(string role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();

        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var user = new AdminUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = role };

        var created = await users.CreateAsync(user, StaffPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, role);
        return email;
    }

    // A browser that has already signed in as a new staff member of the given role
    public async Task<HttpClient> CreateSignedInClientAsync(string role)
    {
        var email = await CreateStaffAsync(role);
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = StaffPassword });
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_uploadsDir))
            Directory.Delete(_uploadsDir, recursive: true);
    }
}
