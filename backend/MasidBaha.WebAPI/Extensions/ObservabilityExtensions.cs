using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace MasidBaha.WebAPI.Extensions;

public static class ObservabilityExtensions
{
    // Structured logs: readable lines while developing, one JSON object per
    // line elsewhere (what log platforms index). Levels come from the Serilog config section.
    public static void UseMasidBahaLogging(this IHostBuilder host)
    {
        host.UseSerilog((context, services, logger) =>
        {
            logger.ReadFrom.Configuration(context.Configuration)
                  .ReadFrom.Services(services)
                  .Enrich.FromLogContext();

            if (context.HostingEnvironment.IsDevelopment())
                logger.WriteTo.Console();
            else
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        });
    }

    // Traces and metrics through OpenTelemetry. Nothing is exported unless an
    // OTLP endpoint is configured, so there's no cost until you point it at a collector.
    public static IServiceCollection AddMasidBahaTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Observability:OtlpEndpoint"] ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("masidbaha-api"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
