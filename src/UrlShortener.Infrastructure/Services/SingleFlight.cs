using System.Collections.Concurrent;
using UrlShortener.Domain.Services;

namespace UrlShortener.Infrastructure.Services;

public class SingleFlight : ISingleFlight
{
    private readonly ConcurrentDictionary<string, Task<object?>> _inFlight = new();

    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        while (true)
        {
            if (_inFlight.TryGetValue(key, out var existingTask))
            {
                var result = await existingTask;
                return result is null ? default! : (T)result;
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inFlight.TryAdd(key, tcs.Task))
            {
                try
                {
                    var res = await action();
                    tcs.TrySetResult(res);
                    return res;
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    throw;
                }
                finally
                {
                    _inFlight.TryRemove(key, out _);
                }
            }
        }
    }
}
