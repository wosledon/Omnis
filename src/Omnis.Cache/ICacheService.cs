using Microsoft.Extensions.Caching.Memory;

namespace Omnis.Cache;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetOrCreateAsync<T>(string key, Func<CacheOptions, Task<T>> factory, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class CacheOptions
{
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

    public TimeSpan? SlidingExpiration { get; set; }

    public double? AbsoluteExpirationInSeconds { get; set; }

    public double? SlidingExpirationInSeconds { get; set; }

    public MemoryCacheEntryOptions ToMemoryCacheEntryOptions()
    {
        var options = new MemoryCacheEntryOptions();

        if (AbsoluteExpirationInSeconds is > 0 or double.PositiveInfinity or double.NegativeInfinity)
        {
            options.SetAbsoluteExpiration(TimeSpan.FromSeconds(AbsoluteExpirationInSeconds!.Value));
        }
        else if (AbsoluteExpirationRelativeToNow is { } absolute)
        {
            options.SetAbsoluteExpiration(absolute);
        }

        if (SlidingExpirationInSeconds is > 0 or double.PositiveInfinity or double.NegativeInfinity)
        {
            options.SetSlidingExpiration(TimeSpan.FromSeconds(SlidingExpirationInSeconds!.Value));
        }
        else if (SlidingExpiration is { } sliding)
        {
            options.SetSlidingExpiration(sliding);
        }

        return options;
    }
}
