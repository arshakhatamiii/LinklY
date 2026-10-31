using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Endpoints;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class LinkEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LinkEndpointsTests(CustomWebApplicationFactory factory)
    {
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
