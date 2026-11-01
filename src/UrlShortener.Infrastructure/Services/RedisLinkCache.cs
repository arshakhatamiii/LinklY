using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UrlShortener.Domain.Services;

namespace UrlShortener.Infrastructure.Services;

public class RedisLinkCache : ILinkCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CacheMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedisLinkCache> _logger;

    private static readonly TimeSpan BackoffDuration = TimeSpan.FromSeconds(5);
    private long _backoffUntilTicks;

    public RedisLinkCache(
        IConnectionMultiplexer redis,
        CacheMetrics metrics,
        TimeProvider timeProvider,
        ILogger<RedisLinkCache> logger)
    {
        _redis = redis;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private bool IsInBackoff()
    {
        return _timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _backoffUntilTicks);
    }

    private void TriggerBackoff(Exception ex, string operation)
    {
        var backoffUntil = _timeProvider.GetUtcNow().Add(BackoffDuration).UtcTicks;
        Interlocked.Exchange(ref _backoffUntilTicks, backoffUntil);
        _metrics.RecordError();
        _logger.LogWarning(ex, "Redis {Operation} failed. Cache entering backoff for {DurationSeconds}s", operation, BackoffDuration.TotalSeconds);
    }

    private static string GetKey(string code) => $"link:{code}";

    public async Task<CachedLink?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        if (IsInBackoff())
        {
            _metrics.RecordMiss();
            return null;
        }

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(GetKey(code));

            if (!value.HasValue)
            {
                _metrics.RecordMiss();
                return null;
            }

            var cached = JsonSerializer.Deserialize<CachedLink>(value.ToString());
            _metrics.RecordHit();
            return cached;
        }
        catch (Exception ex)
        {
            TriggerBackoff(ex, "GET");
            _metrics.RecordMiss();
            return null;
        }
    }

    public async Task SetAsync(string code, string url, DateTime? expiresAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (IsInBackoff() || ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var cached = new CachedLink(url, expiresAtUtc, IsNotFound: false);
            var json = JsonSerializer.Serialize(cached);
            await db.StringSetAsync(GetKey(code), json, ttl);
        }
        catch (Exception ex)
        {
            TriggerBackoff(ex, "SET");
        }
    }

    public async Task SetNegativeAsync(string code, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (IsInBackoff() || ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var cached = new CachedLink(null, null, IsNotFound: true);
            var json = JsonSerializer.Serialize(cached);
            await db.StringSetAsync(GetKey(code), json, ttl);
        }
        catch (Exception ex)
        {
            TriggerBackoff(ex, "SET_NEGATIVE");
        }
    }

    public async Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        if (IsInBackoff())
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(GetKey(code));
        }
        catch (Exception ex)
        {
            TriggerBackoff(ex, "DELETE");
        }
    }
}
