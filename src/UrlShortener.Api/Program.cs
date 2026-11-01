using Microsoft.EntityFrameworkCore;
using Serilog;
using UrlShortener.Api.Endpoints;
using UrlShortener.Domain.Common;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

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
