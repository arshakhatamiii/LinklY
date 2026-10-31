using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api.Endpoints;

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty);

        group.MapPost("/api/links", CreateShortLink)
            .WithName("CreateShortLink")
            .Produces<ShortLinkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{code}", RedirectToLink)
            .WithName("RedirectToLink")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> CreateShortLink(
        [FromBody] CreateShortLinkRequest request,
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        IShortCodeGenerator codeGenerator,
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
        var (id, code) = await codeGenerator.GenerateAsync(cancellationToken);

        var shortLink = ShortLink.Create(id, code, normalizedUrl);
        dbContext.ShortLinks.Add(shortLink);
        await dbContext.SaveChangesAsync(cancellationToken);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var shortUrl = $"{baseUrl}/{code}";

        var response = new ShortLinkResponse(
            shortLink.Code,
            shortUrl,
            shortLink.OriginalUrl,
            shortLink.CreatedAtUtc);

        return Results.Created($"/{code}", response);
    }

    public static async Task<IResult> RedirectToLink(
        string code,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 16)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        var shortLink = await dbContext.ShortLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

        if (shortLink is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not found",
                detail: $"Short link with code '{code}' was not found.");
        }

        return Results.Redirect(shortLink.OriginalUrl, permanent: false);
    }
}

public record CreateShortLinkRequest(string? Url);

public record ShortLinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAtUtc);
