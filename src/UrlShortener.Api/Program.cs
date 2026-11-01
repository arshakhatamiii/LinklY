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

    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "database");

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

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
