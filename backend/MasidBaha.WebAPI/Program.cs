using MasidBaha.Application.Common.Data;
using MasidBaha.Application.FloodReports.CreateReport;
using MasidBaha.Application.FloodReports.GetNearbyReports;
using MasidBaha.WebAPI.Hubs;
using MasidBaha.Application.FloodReports.VoteOnReport;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

builder.Services.AddScoped<ICreateFloodReportService, CreateFloodReportService>();
builder.Services.AddScoped<IGetNearbyReportsService, GetNearbyReportsService>();
builder.Services.AddScoped<IVoteOnReportService, VoteOnReportService>();
builder.Services.AddSignalR();

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

app.UseCors("AllowAngularDev");
app.MapControllers();
app.MapHub<FloodHub>("/hubs/flood");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();