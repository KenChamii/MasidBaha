using System.Threading.RateLimiting;
using MasidBaha.Application.Common.Data;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.FloodReports.ExpireReports;
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
builder.Services.AddScoped<IVoteOnReportService, VoteOnReportService>();
builder.Services.AddScoped<IExpireReportsService, ExpireReportsService>();
builder.Services.AddHostedService<FloodExpiryService>();

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
app.MapControllers();
app.MapHub<FloodHub>("/hubs/flood");

app.Run();