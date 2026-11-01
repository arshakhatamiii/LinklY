using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Endpoints;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class RateLimitingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private HttpRequestMessage CreatePostRequest(string clientIp, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/links")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new CreateShortLinkRequest(url)),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return request;
    }

    [Fact]
    public async Task PostLinks_TwentyFirstRequestInWindow_Returns429WithRetryAfterHeader()
    {
        var clientIp = $"192.168.10.{Random.Shared.Next(1, 250)}";

        // 1. Send 20 requests within the limit
        for (int i = 0; i < 20; i++)
        {
            using var req = CreatePostRequest(clientIp, $"https://example.com/item/{i}");
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        // 2. 21st request exceeds rate limit and must return 429 Too Many Requests
        using var exceedReq = CreatePostRequest(clientIp, "https://example.com/item/exceeded");
        var exceedRes = await _client.SendAsync(exceedReq);

        Assert.Equal(HttpStatusCode.TooManyRequests, exceedRes.StatusCode);
        Assert.True(exceedRes.Headers.Contains("Retry-After"), "Response must contain 'Retry-After' header.");

        var problem = await exceedRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, problem.Status);
        Assert.Equal("Too Many Requests", problem.Title);
    }

    [Fact]
    public async Task RateLimit_DifferentClientIp_IsNotBlocked()
    {
        var blockedIp = $"10.0.1.{Random.Shared.Next(1, 250)}";
        var otherIp = $"10.0.2.{Random.Shared.Next(1, 250)}";

        // Exhaust quota for blockedIp
        for (int i = 0; i < 20; i++)
        {
            using var req = CreatePostRequest(blockedIp, $"https://example.com/blocked/{i}");
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        // Verify blockedIp is throttled
        using var blockedReq = CreatePostRequest(blockedIp, "https://example.com/blocked/throttled");
        var blockedRes = await _client.SendAsync(blockedReq);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedRes.StatusCode);

        // Other IP must still succeed
        using var otherReq = CreatePostRequest(otherIp, "https://example.com/other/success");
        var otherRes = await _client.SendAsync(otherReq);
        Assert.Equal(HttpStatusCode.Created, otherRes.StatusCode);
    }

    [Fact]
    public async Task RedirectEndpoint_IsUnaffectedByPostRateLimiting()
    {
        var clientIp = $"172.16.5.{Random.Shared.Next(1, 250)}";

        // Create a link
        using var createReq = CreatePostRequest(clientIp, "https://example.com/redirect-unaffected");
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        // Exhaust POST quota
        for (int i = 0; i < 19; i++)
        {
            using var req = CreatePostRequest(clientIp, $"https://example.com/fill/{i}");
            await _client.SendAsync(req);
        }

        // 21st POST returns 429
        using var throttledPost = CreatePostRequest(clientIp, "https://example.com/throttled");
        var throttledRes = await _client.SendAsync(throttledPost);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttledRes.StatusCode);

        // Redirect with same client IP must succeed (302 Found)
        using var redirectReq = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
        redirectReq.Headers.Add("X-Forwarded-For", clientIp);
        var redirectRes = await _client.SendAsync(redirectReq);

        Assert.Equal(HttpStatusCode.Found, redirectRes.StatusCode);
        Assert.Equal("https://example.com/redirect-unaffected", redirectRes.Headers.Location?.ToString());
    }
}
