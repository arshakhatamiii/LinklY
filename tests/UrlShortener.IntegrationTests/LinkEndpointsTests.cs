using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Endpoints;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class LinkEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LinkEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task CreateShortLink_WithValidUrl_Returns201Created()
    {
        var request = new CreateShortLinkRequest("https://github.com/dotnet/aspnetcore");

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.Code);
        Assert.Equal("https://github.com/dotnet/aspnetcore", payload.OriginalUrl);
        Assert.Contains(payload.Code, payload.ShortUrl);
        Assert.Equal($"/{payload.Code}", response.Headers.Location?.ToString());
        Assert.Null(payload.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://files.example.com")]
    [InlineData("javascript:alert(1)")]
    public async Task CreateShortLink_WithInvalidUrl_Returns400BadRequest(string invalidUrl)
    {
        var request = new CreateShortLinkRequest(invalidUrl);

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid URL", problem.Title);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
    }

    [Fact]
    public async Task CreateShortLink_WithValidCustomAlias_Returns201CreatedWithAlias()
    {
        var alias = $"my-custom_link-{Guid.NewGuid().ToString("N")[..6]}";
        var request = new CreateShortLinkRequest("https://example.com/custom", Alias: alias);

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(payload);
        Assert.Equal(alias, payload.Code);
        Assert.Equal($"/{alias}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateShortLink_WithDuplicateAlias_Returns409Conflict()
    {
        var alias = $"dup-alias-{Guid.NewGuid().ToString("N")[..6]}";
        var firstRequest = new CreateShortLinkRequest("https://example.com/first", Alias: alias);
        var secondRequest = new CreateShortLinkRequest("https://example.com/second", Alias: alias);

        var firstResponse = await _client.PostAsJsonAsync("/api/links", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/links", secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Alias already in use", problem.Title);
        Assert.Equal((int)HttpStatusCode.Conflict, problem.Status);
        Assert.Contains(alias, problem.Detail);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("health")]
    [InlineData("swagger")]
    [InlineData("API")]
    [InlineData("Healthz")]
    public async Task CreateShortLink_WithReservedWordAlias_Returns400BadRequest(string reservedAlias)
    {
        var request = new CreateShortLinkRequest("https://example.com/reserved", Alias: reservedAlias);

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Reserved Alias", problem.Title);
    }

    [Theory]
    [InlineData("ab")] // Too short (< 3)
    [InlineData("this-alias-is-way-too-long-and-exceeds-thirty-chars")] // Too long (> 30)
    [InlineData("has space")]
    [InlineData("invalid@char")]
    [InlineData("dot.not.allowed")]
    public async Task CreateShortLink_WithInvalidAliasFormat_Returns400BadRequest(string invalidAlias)
    {
        var request = new CreateShortLinkRequest("https://example.com/invalid-alias", Alias: invalidAlias);

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid Alias", problem.Title);
    }

    [Fact]
    public async Task CreateShortLink_WithPastExpiration_Returns400BadRequest()
    {
        var pastDate = _factory.TimeProvider.GetUtcNow().AddHours(-1).UtcDateTime;
        var request = new CreateShortLinkRequest("https://example.com/past", ExpiresAt: pastDate);

        var response = await _client.PostAsJsonAsync("/api/links", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid Expiration Date", problem.Title);
    }

    [Fact]
    public async Task RedirectToLink_BeforeAndAfterExpiration_Returns302Then410Gone()
    {
        var now = _factory.TimeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(30).UtcDateTime;
        var originalUrl = "https://example.com/time-sensitive";

        var createResponse = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest(originalUrl, ExpiresAt: expiresAt));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(created);

        // Before expiration: 302 Found
        var redirectResponse = await _client.GetAsync($"/{created.Code}");
        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Equal(originalUrl, redirectResponse.Headers.Location?.ToString());

        // Fast-forward time past expiration
        _factory.TimeProvider.Advance(TimeSpan.FromMinutes(31));

        // After expiration: 410 Gone
        var expiredResponse = await _client.GetAsync($"/{created.Code}");
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);

        var problem = await expiredResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Link expired", problem.Title);
        Assert.Equal((int)HttpStatusCode.Gone, problem.Status);
    }

    [Fact]
    public async Task RedirectToLink_WithExistingCode_Returns302Redirect()
    {
        var originalUrl = "https://example.com/target-destination";
        var createResponse = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest(originalUrl));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(created);

        var redirectResponse = await _client.GetAsync($"/{created.Code}");

        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Equal(originalUrl, redirectResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RedirectToLink_WithNonExistentCode_Returns404NotFound()
    {
        var response = await _client.GetAsync("/nonexistent-code");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Link not found", problem.Title);
        Assert.Equal((int)HttpStatusCode.NotFound, problem.Status);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200Ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }
}
