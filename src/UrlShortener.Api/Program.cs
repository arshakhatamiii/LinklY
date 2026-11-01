using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using UrlShortener.Api.Endpoints;
using UrlShortener.Domain.Common;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting UrlShortener application...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console());

    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Connection string 'Database' was not found.");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddSingleton<IBase62Encoder, Base62Encoder>();
    builder.Services.AddScoped<IShortCodeGenerator, PostgresSequenceShortCodeGenerator>();
    builder.Services.AddSingleton(TimeProvider.System);

    builder.Services.AddSingleton<CacheMetrics>();
    builder.Services.AddSingleton<ISingleFlight, SingleFlight>();

    builder.Services.AddSingleton<ClickAnalyticsChannel>();
    builder.Services.AddSingleton<IClickTracker>(sp => sp.GetRequiredService<ClickAnalyticsChannel>());
    builder.Services.AddHostedService<ClickAnalyticsBatchProcessor>();

    var cachingEnabled = builder.Configuration.GetValue<bool>("Caching:Enabled", true);
    if (cachingEnabled)
    {
        var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AsyncTimeout = 50;
        redisOptions.SyncTimeout = 50;
        redisOptions.ConnectTimeout = 1000;
        redisOptions.AbortOnConnectFail = false;

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        builder.Services.AddSingleton<ILinkCache, RedisLinkCache>();
    }
    else
    {
        builder.Services.AddSingleton<ILinkCache, NoOpLinkCache>();
    }

    // Configure Forwarded Headers for reverse proxy environments
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Rate Limiting configuration
    var redirectRateLimitingEnabled = builder.Configuration.GetValue<bool>("RateLimiting:Redirect:Enabled", false);
    var redirectPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Redirect:PermitLimit", 1000);
    var redirectWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:Redirect:WindowSeconds", 60);

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/problem+json";

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                var seconds = Math.Max(1, (int)retryAfter.TotalSeconds);
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
            }
            else
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "Rate limit exceeded. Please try again later."
            };

            await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);
        };

        // POST /api/links: 20 req/min per IP (sliding window)
        options.AddPolicy("CreateLinkPolicy", httpContext =>
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
                ?? httpContext.TraceIdentifier;

            return RateLimitPartition.GetSlidingWindowLimiter(clientIp, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
        });

        if (redirectRateLimitingEnabled)
        {
            options.AddPolicy("RedirectPolicy", httpContext =>
            {
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.TraceIdentifier;

                return RateLimitPartition.GetSlidingWindowLimiter(clientIp, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = redirectPermitLimit,
                    Window = TimeSpan.FromSeconds(redirectWindowSeconds),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                });
            });
        }
    });

    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "database");

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseRateLimiter();

    // Migrate database schema on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    app.MapHealthChecks("/health");
    app.MapGet("/internal/stats", (CacheMetrics metrics, IClickTracker clickTracker) =>
        Results.Ok(new
        {
            hits = metrics.Hits,
            misses = metrics.Misses,
            errors = metrics.Errors,
            droppedClicks = clickTracker.DroppedClicks
        }));

    app.MapLinkEndpoints();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program { }
