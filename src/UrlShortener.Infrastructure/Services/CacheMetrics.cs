using System.Diagnostics.Metrics;

namespace UrlShortener.Infrastructure.Services;

public class CacheMetrics
{
    public const string MeterName = "UrlShortener.Cache";

    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;
    private readonly Counter<long> _errorCounter;

    private long _hits;
    private long _misses;
    private long _errors;

    public CacheMetrics()
    {
        var meter = new Meter(MeterName, "1.0.0");
        _hitCounter = meter.CreateCounter<long>("cache_hits", description: "Number of cache hits");
        _missCounter = meter.CreateCounter<long>("cache_misses", description: "Number of cache misses");
        _errorCounter = meter.CreateCounter<long>("cache_errors", description: "Number of cache errors");
    }

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Errors => Interlocked.Read(ref _errors);

    public void RecordHit()
    {
        Interlocked.Increment(ref _hits);
        _hitCounter.Add(1);
    }

    public void RecordMiss()
    {
        Interlocked.Increment(ref _misses);
        _missCounter.Add(1);
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _errors);
        _errorCounter.Add(1);
    }
}
