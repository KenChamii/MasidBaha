using MasidBaha.Application.Common.Data;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.Application.FloodReports.VoteOnReport;
using MasidBaha.Application.FloodReports.ExpireReports;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.WebAPI.BackgroundServices;
using MasidBaha.WebAPI.Middleware;

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
app.MapControllers();
app.MapHub<FloodHub>("/hubs/flood");

app.Run();