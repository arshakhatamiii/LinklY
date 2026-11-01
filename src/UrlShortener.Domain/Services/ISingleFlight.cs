namespace UrlShortener.Domain.Services;

public interface ISingleFlight
{
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action);
}
