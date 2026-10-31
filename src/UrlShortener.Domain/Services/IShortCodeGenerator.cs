namespace UrlShortener.Domain.Services;

public interface IShortCodeGenerator
{
    Task<(long Id, string Code)> GenerateAsync(CancellationToken cancellationToken = default);
}
