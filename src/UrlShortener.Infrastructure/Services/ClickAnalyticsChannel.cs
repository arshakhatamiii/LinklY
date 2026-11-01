using System.Diagnostics.Metrics;
using System.Threading.Channels;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Services;

namespace UrlShortener.Infrastructure.Services;

public class ClickAnalyticsChannel : IClickTracker
{
    private readonly Channel<ClickEvent> _channel;
    private readonly Counter<long> _droppedClicksCounter;
    private long _droppedClicks;

    public const int ChannelCapacity = 10_000;

    public ClickAnalyticsChannel()
    {
        var options = new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<ClickEvent>(options);

        var meter = new Meter("UrlShortener.Analytics", "1.0.0");
        _droppedClicksCounter = meter.CreateCounter<long>("dropped_clicks", description: "Number of dropped click events due to channel buffer capacity");
    }

    public long DroppedClicks => Interlocked.Read(ref _droppedClicks);

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public bool TrackClick(ClickEvent clickEvent)
    {
        // When channel buffer is at capacity, DropOldest will evict the oldest event
        if (_channel.Reader.Count >= ChannelCapacity)
        {
            Interlocked.Increment(ref _droppedClicks);
            _droppedClicksCounter.Add(1);
        }

        return _channel.Writer.TryWrite(clickEvent);
    }
}
