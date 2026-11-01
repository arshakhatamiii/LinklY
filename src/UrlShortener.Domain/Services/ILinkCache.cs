namespace UrlShortener.Domain.Services;

public record CachedLink(long Id, string? Url, DateTime? ExpiresAtUtc, bool IsNotFound = false);

public interface ILinkCache
{
    Task<CachedLink?> GetAsync(string code, CancellationToken cancellationToken = default);
    Task SetAsync(string code, long id, string url, DateTime? expiresAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task SetNegativeAsync(string code, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveAsync(string code, CancellationToken cancellationToken = default);
}
