using System.Diagnostics;
using System.Reflection;
using System.Threading.RateLimiting;
using Asp.Versioning;
using MasidBaha.Application.Common.Options;
using MasidBaha.Application.Persistence;
using MasidBaha.WebAPI.Controllers;
using MasidBaha.WebAPI.ErrorHandling;
using MasidBaha.WebAPI.Filters;
using MasidBaha.WebAPI.Health;
using MasidBaha.WebAPI.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace MasidBaha.WebAPI.Extensions;

public static class WebApiExtensions
{
    public static IServiceCollection AddMasidBahaWebApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Errors: every failure is an RFC 7807 problem response with a trace id
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddControllers(options => options.Filters.Add<FluentValidationFilter>());
        services.AddSignalR();
        services.AddSingleton<ISessionIdentity, SessionIdentity>();

        // /api/v1/... URLs, with the version also documented in Swagger
        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "MasidBaha API",
                Version = "v1",
                Description = "Community flood reporting for the Philippines. Public endpoints need no account; " +
                              "anonymous visitors are identified by a signed session cookie."
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);
        });

        // Short-lived caches for the read endpoints that many clients poll.
        // Writes evict the "reports" tag, so users still see their own changes at once.
        services.AddOutputCache(options =>
        {
            options.AddPolicy("top-reports", builder => builder
                .Expire(TimeSpan.FromSeconds(15))
                .SetVaryByQuery("*")
                .SetVaryByHeader("Origin") // CORS headers are part of the cached response
                .Tag(FloodReportsController.CacheTag));

            options.AddPolicy("heatmap", builder => builder
                .Expire(TimeSpan.FromSeconds(60))
                .SetVaryByQuery("*")
                .SetVaryByHeader("Origin")
                .Tag(FloodReportsController.CacheTag));
        });

        services.AddMasidBahaCors(configuration);
        services.AddMasidBahaRateLimiting();
        services.AddMasidBahaForwardedHeaders(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<MasidBahaDbContext>("database", tags: ["ready"])
            .AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);

        return services;
    }

    private static void AddMasidBahaCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy("Frontend", policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials())); // the session cookie travels with credentialed requests
    }

    // Rate limiting is keyed per client IP, since reports and votes have no login.
    // Limits come from the RateLimits options rather than being hardcoded here.
    private static void AddMasidBahaRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many requests",
                            Detail = "You are doing that too often. Please wait a moment and try again."
                        }
                    });
            };
        });

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitOptions>>((limiter, limits) =>
            {
                AddPolicy(limiter, "report-writes", limits.Value.ReportWrites);
                AddPolicy(limiter, "vote-writes", limits.Value.VoteWrites);
                AddPolicy(limiter, "push-writes", limits.Value.PushWrites);
            });
    }

    private static void AddPolicy(RateLimiterOptions limiter, string name, RateLimitPolicyOptions policy)
    {
        limiter.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                QueueLimit = 0
            }));
    }

    // Behind a reverse proxy the socket address is the proxy's, so rate limiting
    // would see one "client". When enabled, trust X-Forwarded-* from the platform's
    // load balancer instead. Only turn this on if the API is not directly exposed.
    private static void AddMasidBahaForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>($"{SecurityOptions.Section}:TrustForwardedHeaders"))
            return;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }
}
