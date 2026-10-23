using FluentValidation;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Admin.Audit;
using MasidBaha.Application.Admin.Bans;
using MasidBaha.Application.Admin.Broadcasts;
using MasidBaha.Application.Admin.Dashboard;
using MasidBaha.Application.Admin.Moderation;
using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Admin.Users;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetReport;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.Persistence;
using MasidBaha.Application.Photos.UploadPhoto;
using MasidBaha.Application.PushNotifications;
using MasidBaha.Application.Weather;
using MasidBaha.Application.PushNotifications.WatchAreas;
using MasidBaha.Application.Trust;
using MasidBaha.WebAPI.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace MasidBaha.WebAPI.Extensions;

public static class ServiceCollectionExtensions
{
    // Binds a config section to a typed options class and validates it at
    // startup, so a bad value fails the boot instead of surfacing in a request
    private static void AddValidatedOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    public static IServiceCollection AddMasidBahaOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ReportOptions>(configuration, ReportOptions.Section);
        services.AddValidatedOptions<ExpiryOptions>(configuration, ExpiryOptions.Section);
        services.AddValidatedOptions<PushOptions>(configuration, PushOptions.Section);
        services.AddValidatedOptions<VapidOptions>(configuration, VapidOptions.Section);
        services.AddValidatedOptions<PhotoOptions>(configuration, PhotoOptions.Section);
        services.AddValidatedOptions<RateLimitOptions>(configuration, RateLimitOptions.Section);
        services.AddValidatedOptions<GeocodingOptions>(configuration, GeocodingOptions.Section);
        services.AddValidatedOptions<WeatherOptions>(configuration, WeatherOptions.Section);
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.Section);
        services.AddValidatedOptions<SecurityOptions>(configuration, SecurityOptions.Section);
        services.AddValidatedOptions<AdminAuthOptions>(configuration, AdminAuthOptions.Section);
        return services;
    }

    // PostgreSQL + PostGIS. Table and column names are snake_case by convention.
    public static IServiceCollection AddMasidBahaPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MasidBahaDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:MasidBahaDb is not set. Run scripts/init-dev-secrets.ps1 or see appsettings.Example.json.");

        services.AddDbContext<MasidBahaDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention());

        return services;
    }

    // One registration per feature slice, plus the background workers
    public static IServiceCollection AddMasidBahaFeatures(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateFloodReportRequestValidator>();

        services.AddScoped<ICreateFloodReportService, CreateFloodReportService>();
        services.AddScoped<IGetNearbyReportsService, GetNearbyReportsService>();
        services.AddScoped<IGetTopReportsService, GetTopReportsService>();
        services.AddScoped<IGetHeatmapDataService, GetHeatmapDataService>();
        services.AddScoped<IGetReportByIdService, GetReportByIdService>();
        services.AddScoped<IVoteOnReportService, VoteOnReportService>();
        services.AddScoped<IExpireReportsService, ExpireReportsService>();
        services.AddScoped<IAdminReportsService, AdminReportsService>();
        services.AddScoped<ISessionTrustService, SessionTrustService>();

        // Back-office
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddSingleton<IRuntimeSettings, RuntimeSettings>();
        services.AddScoped<IBanService, BanService>();
        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAdminUsersService, AdminUsersService>();
        services.AddScoped<IBroadcastService, BroadcastService>();
        services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
        services.AddScoped<IWatchAreaService, WatchAreaService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IUploadPhotoService, UploadPhotoService>();

        // Web push: one shared client (and HttpClient pipeline) for every broadcast
        services.AddHttpClient<WebPushClient>();
        services.AddSingleton<IPushQueue, PushQueue>();

        services.AddHostedService<FloodExpiryService>();
        services.AddHostedService<PushDispatchService>();
        services.AddHostedService<SettingsRefreshService>();
        return services;
    }

    // Photos live on local disk by default, or in an S3-compatible bucket
    // (Cloudflare R2, Supabase Storage, MinIO) when Storage:Provider is "S3"
    public static IServiceCollection AddMasidBahaStorage(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration[$"{StorageOptions.Section}:Provider"];

        if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPhotoStorageService, S3PhotoStorageService>();
        }
        else
        {
            // Resolved here because only the host knows the content root in both
            // Development and published builds
            var uploadsRootPath = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
            services.AddSingleton<IPhotoStorageService>(_ => new LocalDiskPhotoStorageService(uploadsRootPath));
        }

        services.AddSingleton<IPhotoUrlPolicy, PhotoUrlPolicy>();
        return services;
    }

    // Reverse geocoding (OpenStreetMap Nominatim) tags each report with
    // Region/Province/City. Cached, rate-gated, and wrapped in a resilience
    // pipeline (retry, circuit breaker, timeouts) because it's a free external service.
    // Typhoon tracks from GDACS: one cached copy shared by every visitor, with the same kind of
    // resilience pipeline as the geocoder because it is also a free outside service
    public static IServiceCollection AddMasidBahaWeather(this IServiceCollection services, IConfiguration configuration)
    {
        var weather = configuration.GetSection(WeatherOptions.Section).Get<WeatherOptions>() ?? new WeatherOptions();

        services.AddHttpClient<ITyphoonSource, GdacsTyphoonSource>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MasidBaha/1.0 (community flood reporting)");
            })
            .AddStandardResilienceHandler(options =>
            {
                var attempt = TimeSpan.FromSeconds(weather.TimeoutSeconds);
                options.AttemptTimeout.Timeout = attempt;
                options.TotalRequestTimeout.Timeout = attempt * 3;
                options.Retry.MaxRetryAttempts = 1;
                options.CircuitBreaker.SamplingDuration = attempt * 3; // at least twice the attempt timeout
            });
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITyphoonStore, DbTyphoonStore>();
        services.AddSingleton<ITyphoonService, TyphoonService>();
        services.AddHostedService<TyphoonRefreshService>();

        return services;
    }

    public static IServiceCollection AddMasidBahaGeocoding(this IServiceCollection services, IConfiguration configuration)
    {
        var geocoding = configuration.GetSection(GeocodingOptions.Section).Get<GeocodingOptions>() ?? new GeocodingOptions();

        services.AddMemoryCache();
        services.AddSingleton<GeocodingRateGate>();

        services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client =>
            {
                client.BaseAddress = new Uri(geocoding.BaseAddress);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(geocoding.UserAgent);
            })
            .AddStandardResilienceHandler(options =>
            {
                var attempt = TimeSpan.FromSeconds(geocoding.TimeoutSeconds);
                options.AttemptTimeout.Timeout = attempt;
                options.TotalRequestTimeout.Timeout = attempt * 3;
                options.Retry.MaxRetryAttempts = 2;
                options.CircuitBreaker.SamplingDuration = attempt * 3; // must be at least twice the attempt timeout
            });

        return services;
    }
}
