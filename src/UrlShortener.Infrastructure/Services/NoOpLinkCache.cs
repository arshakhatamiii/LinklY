using UrlShortener.Domain.Services;

namespace UrlShortener.Infrastructure.Services;

public class NoOpLinkCache : ILinkCache
{
    public Task<CachedLink?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CachedLink?>(null);
    }

    public Task SetAsync(string code, string url, DateTime? expiresAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetNegativeAsync(string code, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
