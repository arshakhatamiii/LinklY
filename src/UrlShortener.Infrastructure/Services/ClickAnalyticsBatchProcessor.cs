using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrlShortener.Domain.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Services;

public class ClickAnalyticsBatchProcessor : BackgroundService
{
    private readonly ClickAnalyticsChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClickAnalyticsBatchProcessor> _logger;

    public const int MaxBatchSize = 500;
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    public ClickAnalyticsBatchProcessor(
        ClickAnalyticsChannel channel,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ClickAnalyticsBatchProcessor> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>(MaxBatchSize);
        var reader = _channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await reader.WaitToReadAsync(stoppingToken))
                {
                    var flushDeadline = _timeProvider.GetUtcNow().Add(FlushInterval);

                    while (batch.Count < MaxBatchSize && reader.TryRead(out var evt))
                    {
                        batch.Add(evt);
                    }

                    while (batch.Count < MaxBatchSize && _timeProvider.GetUtcNow() < flushDeadline && !stoppingToken.IsCancellationRequested)
                    {
                        var remaining = flushDeadline - _timeProvider.GetUtcNow();
                        if (remaining <= TimeSpan.Zero)
                        {
                            break;
                        }

                        using var timeoutCts = new CancellationTokenSource(remaining);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                        try
                        {
                            if (await reader.WaitToReadAsync(linkedCts.Token))
                            {
                                while (batch.Count < MaxBatchSize && reader.TryRead(out var evt))
                                {
                                    batch.Add(evt);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    if (batch.Count > 0)
                    {
                        await FlushBatchAsync(batch, CancellationToken.None);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in click analytics batch processing.");
                await Task.Delay(500, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        // Graceful shutdown flush
        await FlushRemainingAsync(batch);
    }

    private async Task FlushRemainingAsync(List<ClickEvent> batch)
    {
        var reader = _channel.Reader;
        while (reader.TryRead(out var evt))
        {
            batch.Add(evt);
            if (batch.Count >= MaxBatchSize)
            {
                await FlushBatchAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
            batch.Clear();
        }
    }

    private async Task FlushBatchAsync(List<ClickEvent> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await dbContext.ClickEvents.AddRangeAsync(batch, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Successfully flushed {Count} click events to database.", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} click events to database.", batch.Count);
        }
    }
}
