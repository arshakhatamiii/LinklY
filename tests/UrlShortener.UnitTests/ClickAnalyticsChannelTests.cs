using UrlShortener.Domain.Entities;
using UrlShortener.Infrastructure.Services;
using Xunit;

namespace UrlShortener.UnitTests;

public class ClickAnalyticsChannelTests
{
    [Fact]
    public void TrackClick_WhenUnderCapacity_DoesNotDropClicks()
    {
        var channel = new ClickAnalyticsChannel();
        var click = ClickEvent.Create(1, DateTime.UtcNow, "https://google.com", "Mozilla/5.0");

        var tracked = channel.TrackClick(click);

        Assert.True(tracked);
        Assert.Equal(0, channel.DroppedClicks);
    }

    [Fact]
    public void TrackClick_WhenBufferExceedsCapacity_IncrementsDroppedClicksCounter()
    {
        var channel = new ClickAnalyticsChannel();

        // Fill channel to capacity (10,000)
        for (int i = 0; i < ClickAnalyticsChannel.ChannelCapacity; i++)
        {
            channel.TrackClick(ClickEvent.Create(1, DateTime.UtcNow, null, null));
        }

        Assert.Equal(0, channel.DroppedClicks);

        // Add 5 more items which trigger drops
        for (int i = 0; i < 5; i++)
        {
            channel.TrackClick(ClickEvent.Create(1, DateTime.UtcNow, null, null));
        }

        Assert.Equal(5, channel.DroppedClicks);
    }

    [Fact]
    public void ClickEvent_TruncatesReferrerAndUserAgentTo200Chars()
    {
        var longReferrer = new string('a', 300);
        var longUserAgent = new string('b', 300);

        var evt = ClickEvent.Create(1, DateTime.UtcNow, longReferrer, longUserAgent);

        Assert.NotNull(evt.Referrer);
        Assert.NotNull(evt.UserAgent);
        Assert.Equal(200, evt.Referrer.Length);
        Assert.Equal(200, evt.UserAgent.Length);
    }
}
