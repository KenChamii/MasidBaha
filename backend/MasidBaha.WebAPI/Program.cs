using MasidBaha.Application.Admin.Settings;
using MasidBaha.Application.Admin;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using MasidBaha.WebAPI.Extensions;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.WebAPI.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseMasidBahaLogging();

builder.Services
    .AddMasidBahaOptions(builder.Configuration)
    .AddMasidBahaPersistence(builder.Configuration)
    .AddMasidBahaFeatures()
    .AddMasidBahaStorage(builder.Configuration, builder.Environment)
    .AddMasidBahaGeocoding(builder.Configuration)
    .AddMasidBahaWebApi(builder.Configuration)
    .AddMasidBahaTelemetry(builder.Configuration);

var app = builder.Build();

// Fail at boot, not on the first visitor, if the session signing key is missing
app.Services.GetRequiredService<ISessionIdentity>();

// Apply pending EF migrations at startup (on by default), and optionally load
// demo reports. Both are config flags so production can opt out.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MasidBahaDbContext>();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
        await db.Database.MigrateAsync();

    // Roles always; the first SuperAdmin only if configured and none exists yet
    await AdminSeeder.SeedAsync(scope.ServiceProvider);

    // Staff-made setting overrides take effect from the first request
    await app.Services.GetRequiredService<IRuntimeSettings>().ReloadAsync();

    if (app.Configuration.GetValue("Database:SeedDemoData", false))
        await DemoDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IOptions<ReportOptions>>().Value.Lifetime);
}

var security = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

if (security.TrustForwardedHeaders)
    app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages(); // bodyless 404/405/etc. also come back as problem details

if (security.RequireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

// Serves wwwroot/uploads/** at /uploads/** (what LocalDiskPhotoStorageService writes).
// Not used once photos live in an S3 bucket.
app.UseStaticFiles();

// The OpenAPI document and Scalar reference are public; the Swagger UI is dev-only
app.UseSwagger();
if (app.Environment.IsDevelopment())
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "MasidBaha API v1"));

app.MapControllers();
app.MapHub<FloodHub>("/hubs/flood");
app.MapScalarApiReference("/docs", options => options
    .WithTitle("MasidBaha API")
    .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json"));

// Liveness: the process is up. Readiness: the database and photo storage answer.
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        });
    },
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.Run();

// Lets the integration tests host the real app through WebApplicationFactory<Program>
public partial class Program;
