using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api.Endpoints;

public static partial class LinkEndpoints
{
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{3,30}$")]
    private static partial Regex AliasRegex();

    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty);

        group.MapPost("/api/links", CreateShortLink)
            .WithName("CreateShortLink")
            .RequireRateLimiting("CreateLinkPolicy")
            .Produces<ShortLinkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        var redirectEndpoint = group.MapGet("/{code}", RedirectToLink)
            .WithName("RedirectToLink")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        var config = app.ServiceProvider.GetRequiredService<IConfiguration>();
        if (config.GetValue<bool>("RateLimiting:Redirect:Enabled", false))
        {
            redirectEndpoint.RequireRateLimiting("RedirectPolicy");
        }

        group.MapGet("/api/links/{code}/stats", GetLinkStats)
            .WithName("GetLinkStats")
            .Produces<LinkStatsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> CreateShortLink(
        [FromBody] CreateShortLinkRequest request,
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        IShortCodeGenerator codeGenerator,
        ILinkCache linkCache,
        IConfiguration configuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Url))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid URL",
                detail: "The 'url' field is required and cannot be empty.");
        }

        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid URL",
                detail: "The provided URL is not a valid absolute HTTP or HTTPS URL.");
        }

        var normalizedUrl = uri.AbsoluteUri;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        DateTime? expiresAtUtc = null;
        if (request.ExpiresAt.HasValue)
        {
            expiresAtUtc = DateTime.SpecifyKind(request.ExpiresAt.Value, DateTimeKind.Utc);
            if (expiresAtUtc.Value <= nowUtc)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Expiration Date",
                    detail: "The expiration date must be in the future.");
            }
        }

        string code;
        long id;

        if (!string.IsNullOrWhiteSpace(request.Alias))
        {
            var alias = request.Alias.Trim();

            if (alias.Length is < 3 or > 30)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Alias",
                    detail: "The alias must be between 3 and 30 characters in length.");
            }

            if (!AliasRegex().IsMatch(alias))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Alias",
                    detail: "The alias can only contain alphanumeric characters, underscores, and hyphens.");
            }

            var reservedWords = configuration
                .GetSection("ShortLinkSettings:ReservedWords")
                .Get<string[]>() ?? ["api", "health", "swagger", "healthz", "metrics"];

            if (reservedWords.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Reserved Alias",
                    detail: $"The alias '{alias}' is reserved and cannot be used.");
            }

            var (seqId, _) = await codeGenerator.GenerateAsync(cancellationToken);
            id = seqId;
            code = alias;
        }
        else
        {
            var (seqId, generatedCode) = await codeGenerator.GenerateAsync(cancellationToken);
            id = seqId;
            code = generatedCode;
        }

        var shortLink = ShortLink.Create(id, code, normalizedUrl, nowUtc, expiresAtUtc);
        dbContext.ShortLinks.Add(shortLink);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx &&
                                            pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Alias already in use",
                detail: $"The alias '{code}' is already in use.");
        }

        await linkCache.RemoveAsync(code, cancellationToken);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var shortUrl = $"{baseUrl}/{code}";

        var response = new ShortLinkResponse(
            shortLink.Code,
            shortUrl,
            shortLink.OriginalUrl,
            shortLink.CreatedAtUtc,
            shortLink.ExpiresAtUtc);

        return Results.Created($"/{code}", response);
    }

    public static async Task<IResult> RedirectToLink(
        string code,
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        ILinkCache linkCache,
        ISingleFlight singleFlight,
        IClickTracker clickTracker,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 30)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        var referrer = httpContext.Request.Headers.Referer.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var cached = await linkCache.GetAsync(code, cancellationToken);
        if (cached is not null)
        {
            if (cached.IsNotFound)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Link not found",
                    detail: $"Short link with code '{code}' was not found.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (cached.ExpiresAtUtc.HasValue && cached.ExpiresAtUtc.Value <= now)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status410Gone,
                    title: "Link expired",
                    detail: $"Short link with code '{code}' has expired.");
            }

            clickTracker.TrackClick(ClickEvent.Create(cached.Id, now, referrer, userAgent));
            return Results.Redirect(cached.Url!, permanent: false);
        }

        var entry = await singleFlight.ExecuteAsync(code, async () =>
        {
            var shortLink = await dbContext.ShortLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

            if (shortLink is null)
            {
                await linkCache.SetNegativeAsync(code, TimeSpan.FromSeconds(60), cancellationToken);
                return new CachedLink(0, null, null, IsNotFound: true);
            }

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            TimeSpan ttl = TimeSpan.FromHours(24);
            if (shortLink.ExpiresAtUtc.HasValue)
            {
                var remaining = shortLink.ExpiresAtUtc.Value - nowUtc;
                if (remaining < ttl)
                {
                    ttl = remaining;
                }
            }

            if (!shortLink.ExpiresAtUtc.HasValue || shortLink.ExpiresAtUtc.Value > nowUtc)
            {
                await linkCache.SetAsync(shortLink.Code, shortLink.Id, shortLink.OriginalUrl, shortLink.ExpiresAtUtc, ttl, cancellationToken);
            }

            return new CachedLink(shortLink.Id, shortLink.OriginalUrl, shortLink.ExpiresAtUtc, IsNotFound: false);
        });

        if (entry.IsNotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        var nowUtcCheck = timeProvider.GetUtcNow().UtcDateTime;
        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= nowUtcCheck)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Link expired",
                detail: $"Short link with code '{code}' has expired.");
        }

        clickTracker.TrackClick(ClickEvent.Create(entry.Id, nowUtcCheck, referrer, userAgent));
        return Results.Redirect(entry.Url!, permanent: false);
    }

    public static async Task<IResult> GetLinkStats(
        string code,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 30)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        var link = await dbContext.ShortLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

        if (link is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var thirtyDaysAgo = nowUtc.AddDays(-30);

        var totalClicks = await dbContext.ClickEvents
            .Where(x => x.LinkId == link.Id)
            .LongCountAsync(cancellationToken);

        var rawDailyClicks = await dbContext.ClickEvents
            .Where(x => x.LinkId == link.Id && x.ClickedAtUtc >= thirtyDaysAgo)
            .GroupBy(x => x.ClickedAtUtc.Date)
            .Select(g => new { Date = g.Key, Count = g.LongCount() })
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        var clicksPerDay = rawDailyClicks
            .Select(x => new DailyClickCount(DateOnly.FromDateTime(x.Date), x.Count))
            .ToList();

        return Results.Ok(new LinkStatsResponse(link.Code, totalClicks, clicksPerDay));
    }
}

public record DailyClickCount(DateOnly Date, long Count);

public record LinkStatsResponse(
    string Code,
    long TotalClicks,
    IReadOnlyList<DailyClickCount> ClicksPerDay);

public record CreateShortLinkRequest(string? Url, string? Alias = null, DateTime? ExpiresAt = null);

public record ShortLinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc = null);
