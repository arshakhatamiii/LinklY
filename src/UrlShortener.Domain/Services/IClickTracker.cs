using UrlShortener.Domain.Entities;

namespace UrlShortener.Domain.Services;

public interface IClickTracker
{
    bool TrackClick(ClickEvent clickEvent);
    long DroppedClicks { get; }
}
