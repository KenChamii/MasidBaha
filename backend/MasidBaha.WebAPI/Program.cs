using System.Threading.RateLimiting;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.Common.Geocoding;
using MasidBaha.Application.Common.Storage;
using MasidBaha.Application.Photos.UploadPhoto;
using MasidBaha.Application.PushNotifications;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.GetTopReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.Application.FloodReports.GetHeatmapData;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Trust;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.WebAPI.BackgroundServices;
using MasidBaha.WebAPI.Middleware;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<ICreateFloodReportService, CreateFloodReportService>();
builder.Services.AddScoped<IGetNearbyReportsService, GetNearbyReportsService>();
builder.Services.AddScoped<IGetTopReportsService, GetTopReportsService>();
builder.Services.AddScoped<IVoteOnReportService, VoteOnReportService>();
builder.Services.AddScoped<IExpireReportsService, ExpireReportsService>();
builder.Services.AddScoped<IGetHeatmapDataService, GetHeatmapDataService>();
builder.Services.AddScoped<IAdminReportsService, AdminReportsService>();
builder.Services.AddScoped<ISessionTrustService, SessionTrustService>();
builder.Services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddHostedService<FloodExpiryService>();

// Photo storage — swap LocalDiskPhotoStorageService for an
// AzureBlobPhotoStorageService (same IPhotoStorageService interface) once
// ready to move off local disk. No other code needs to change when that happens.
// Path is resolved here (not inside the service) because only the host layer
// reliably knows the content root in both Development and published builds.
var uploadsRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
builder.Services.AddSingleton<IPhotoStorageService>(_ => new LocalDiskPhotoStorageService(uploadsRootPath));
builder.Services.AddScoped<IUploadPhotoService, UploadPhotoService>();

// Reverse geocoding (OpenStreetMap Nominatim) — tags each report with
// Region/Province/City so the top-reports list can be scoped nationally,
// regionally, provincially, or per city.
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MasidBaha/1.0 (community flood reporting)");
});

// Rate limiting: keyed per client IP, since reports/votes have no auth yet.
// "report-writes" guards report creation (spammy pin-dropping).
// "vote-writes" is a bit looser since confirm/deny is meant to be quick and frequent.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("report-writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("vote-writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("push-writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("AllowAngularDev");
app.UseRateLimiter();

// Gates /api/admin/** behind the X-Admin-Key header. Must run after CORS
// (so preflight/browser calls from the Angular admin page still work) but
// before MapControllers (so unauthorized requests never reach the action).
app.UseMiddleware<AdminAuthMiddleware>();

// Serves wwwroot/uploads/** at /uploads/** — this is what LocalDiskPhotoStorageService
// writes into. Not needed once move to Azure Blob (files are served from Azure then).
app.UseStaticFiles();

app.MapControllers();
app.MapHub<FloodHub>("/hubs/flood");

app.Run();