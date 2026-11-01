namespace UrlShortener.Domain.Entities;

public class ClickEvent
{
    public long Id { get; set; }
    public long LinkId { get; set; }
    public DateTime ClickedAtUtc { get; set; }
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }

    public static ClickEvent Create(long linkId, DateTime clickedAtUtc, string? referrer, string? userAgent)
    {
        return new ClickEvent
        {
            LinkId = linkId,
            ClickedAtUtc = clickedAtUtc,
            Referrer = Truncate(referrer, 200),
            UserAgent = Truncate(userAgent, 200)
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
