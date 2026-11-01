using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UrlShortener.Api.Endpoints;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Services;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Services;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class ClickAnalyticsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClickAnalyticsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Redirect_PersistsClicksAsynchronously_AndStatsReflectsCounts()
    {
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/analytics-test"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        // Send 3 redirect requests with custom headers
        using var req1 = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
        req1.Headers.Referrer = new Uri("https://referrer1.com");
        req1.Headers.UserAgent.ParseAdd("Agent-1");
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Found, res1.StatusCode);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
        req2.Headers.Referrer = new Uri("https://referrer2.com");
        req2.Headers.UserAgent.ParseAdd("Agent-2");
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Found, res2.StatusCode);

        using var req3 = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Found, res3.StatusCode);

        // Wait for the 2-second background flush interval
        await Task.Delay(2500);

        // Query stats endpoint
        var statsRes = await _client.GetAsync($"/api/links/{link.Code}/stats");
        Assert.Equal(HttpStatusCode.OK, statsRes.StatusCode);
        var stats = await statsRes.Content.ReadFromJsonAsync<LinkStatsResponse>();

        Assert.NotNull(stats);
        Assert.Equal(link.Code, stats.Code);
        Assert.Equal(3, stats.TotalClicks);
        Assert.Single(stats.ClicksPerDay);
        Assert.Equal(3, stats.ClicksPerDay[0].Count);
    }

    [Fact]
    public async Task GetStats_WhenLinkDoesNotExist_Returns404NotFound()
    {
        var res = await _client.GetAsync("/api/links/nonexistent-code/stats");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Link not found", problem.Title);
    }

    [Fact]
    public async Task BatchProcessor_FlushesImmediately_WhenBatchReaches500Events()
    {
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/batch-500"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbLink = await dbContext.ShortLinks.FirstAsync(x => x.Code == link.Code);

        var tracker = _factory.Services.GetRequiredService<ClickAnalyticsChannel>();

        // Enqueue exactly 500 events
        for (int i = 0; i < 500; i++)
        {
            tracker.TrackClick(ClickEvent.Create(dbLink.Id, DateTime.UtcNow, "https://batch.com", "Tester"));
        }

        // Wait a short duration (far less than 2s interval) to verify batch size triggered flush
        await Task.Delay(300);

        var count = await dbContext.ClickEvents.CountAsync(x => x.LinkId == dbLink.Id);
        Assert.Equal(500, count);
    }

    [Fact]
    public async Task BatchProcessor_GracefulShutdown_FlushesRemainingEvents()
    {
        var createRes = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/shutdown-flush"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var link = await createRes.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(link);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbLink = await dbContext.ShortLinks.FirstAsync(x => x.Code == link.Code);

        var channel = new ClickAnalyticsChannel();
        for (int i = 0; i < 15; i++)
        {
            channel.TrackClick(ClickEvent.Create(dbLink.Id, DateTime.UtcNow, "https://shutdown.com", "ShutdownAgent"));
        }

        var processor = new ClickAnalyticsBatchProcessor(
            channel,
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClickAnalyticsBatchProcessor>.Instance);

        // Start processor and immediately stop it to test graceful shutdown flush
        using var cts = new CancellationTokenSource();
        var processTask = processor.StartAsync(cts.Token);
        await processor.StopAsync(CancellationToken.None);
        await processTask;

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await verifyDb.ClickEvents.CountAsync(x => x.LinkId == dbLink.Id);
        Assert.Equal(15, count);
    }
}
