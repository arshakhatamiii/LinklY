using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Endpoints;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class RedisResilienceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RedisResilienceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task RedisStopped_RedirectStillWorksFromDatabase()
    {
        // 1. Create a link
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/resilience-test"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        // 2. Stop Redis container to simulate Redis outage
        await _factory.RedisContainer.StopAsync();

        // 3. Requesting the link must still succeed with 302 from PostgreSQL without throwing 500
        var redirectRes = await _client.GetAsync($"/{link.Code}");
        Assert.Equal(HttpStatusCode.Found, redirectRes.StatusCode);
        Assert.Equal("https://example.com/resilience-test", redirectRes.Headers.Location?.ToString());

        // 4. Verify stats shows errors logged
        var statsRes = await _client.GetAsync("/internal/stats");
        var stats = await statsRes.Content.ReadFromJsonAsync<CacheStatsResponse>();
        Assert.NotNull(stats);
        Assert.True(stats.Errors > 0, "Redis error count should be greater than zero when Redis is down.");
    }
}
