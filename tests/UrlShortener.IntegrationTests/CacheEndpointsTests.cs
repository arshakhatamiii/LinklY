using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Endpoints;
using Xunit;

namespace UrlShortener.IntegrationTests;

public record CacheStatsResponse(long Hits, long Misses, long Errors);

public class CacheEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CacheEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<CacheStatsResponse> GetStatsAsync()
    {
        var response = await _client.GetAsync("/internal/stats");
        response.EnsureSuccessStatusCode();
        var stats = await response.Content.ReadFromJsonAsync<CacheStatsResponse>();
        Assert.NotNull(stats);
        return stats;
    }

    [Fact]
    public async Task MissPathPopulatesCache_AndSecondCallHitsCache()
    {
        // 1. Create a new link
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/cache-hit-test"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        var initialStats = await GetStatsAsync();

        // 2. First GET should be a cache miss and populate cache
        var get1 = await _client.GetAsync($"/{link.Code}");
        Assert.Equal(HttpStatusCode.Found, get1.StatusCode);
        Assert.Equal("https://example.com/cache-hit-test", get1.Headers.Location?.ToString());

        var afterMissStats = await GetStatsAsync();
        Assert.Equal(initialStats.Misses + 1, afterMissStats.Misses);

        // 3. Second GET should be a cache hit
        var get2 = await _client.GetAsync($"/{link.Code}");
        Assert.Equal(HttpStatusCode.Found, get2.StatusCode);
        Assert.Equal("https://example.com/cache-hit-test", get2.Headers.Location?.ToString());

        var afterHitStats = await GetStatsAsync();
        Assert.Equal(afterMissStats.Hits + 1, afterHitStats.Hits);
    }

    [Fact]
    public async Task NegativeCaching_CachesNotFound_AndSubsequentCallsHitCache()
    {
        var nonExistentCode = $"nonexistent-{Guid.NewGuid().ToString("N")[..8]}";
        var initialStats = await GetStatsAsync();

        // 1. First call misses cache and DB -> caches negative entry
        var res1 = await _client.GetAsync($"/{nonExistentCode}");
        Assert.Equal(HttpStatusCode.NotFound, res1.StatusCode);

        var statsAfterFirst = await GetStatsAsync();
        Assert.Equal(initialStats.Misses + 1, statsAfterFirst.Misses);

        // 2. Second call should hit the negative cache
        var res2 = await _client.GetAsync($"/{nonExistentCode}");
        Assert.Equal(HttpStatusCode.NotFound, res2.StatusCode);

        var statsAfterSecond = await GetStatsAsync();
        Assert.Equal(statsAfterFirst.Hits + 1, statsAfterSecond.Hits);
    }

    [Fact]
    public async Task AliasCreation_ClearsNegativeCacheEntry()
    {
        var alias = $"alias-clear-{Guid.NewGuid().ToString("N")[..8]}";

        // 1. Request nonexistent alias -> caches negative entry
        var res1 = await _client.GetAsync($"/{alias}");
        Assert.Equal(HttpStatusCode.NotFound, res1.StatusCode);

        // 2. Create link using the same alias
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/alias-cleared", Alias: alias));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // 3. Next GET should find the newly created link (302 redirect), NOT the negative cache (404)
        var res2 = await _client.GetAsync($"/{alias}");
        Assert.Equal(HttpStatusCode.Found, res2.StatusCode);
        Assert.Equal("https://example.com/alias-cleared", res2.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ExpiredLink_Returns410GoneFromCache_WhenReadAfterExpiration()
    {
        var now = _factory.TimeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(30).UtcDateTime;

        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest(
            "https://example.com/expiring-cache",
            ExpiresAt: expiresAt));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        // 1. Hit while still active -> 302 Found, populates cache
        var res1 = await _client.GetAsync($"/{link.Code}");
        Assert.Equal(HttpStatusCode.Found, res1.StatusCode);

        // 2. Advance time past expiration
        _factory.TimeProvider.Advance(TimeSpan.FromHours(1));

        // 3. Cached entry must evaluate read-time expiry and return 410 Gone
        var res2 = await _client.GetAsync($"/{link.Code}");
        Assert.Equal(HttpStatusCode.Gone, res2.StatusCode);
    }
}
