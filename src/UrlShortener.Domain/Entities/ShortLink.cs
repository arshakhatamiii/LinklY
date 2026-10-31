namespace UrlShortener.Domain.Entities;

public class ShortLink
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public static ShortLink Create(long id, string code, string originalUrl)
    {
        return new ShortLink
        {
            Id = id,
            Code = code,
            OriginalUrl = originalUrl,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
