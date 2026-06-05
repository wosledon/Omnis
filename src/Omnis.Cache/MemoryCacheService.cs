using Microsoft.Extensions.Caching.Memory;

namespace Omnis.Cache;

public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;

    public MemoryCacheService(IMemoryCache memoryCache)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        _memoryCache = memoryCache;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _memoryCache.TryGetValue(key, out T? value)
            ? Task.FromResult(value)
            : Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        cancellationToken.ThrowIfCancellationRequested();

        _memoryCache.Set(key, value, options?.ToMemoryCacheEntryOptions() ?? new MemoryCacheEntryOptions());

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _memoryCache.Remove(key);

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CacheOptions, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CacheOptions();
        var value = await factory(options).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        return await _memoryCache.GetOrCreateAsync(key, entry =>
        {
            entry.SetAbsoluteExpiration(options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromSeconds(options.AbsoluteExpirationInSeconds ?? 0));
            entry.SetSlidingExpiration(options.SlidingExpiration ?? TimeSpan.FromSeconds(options.SlidingExpirationInSeconds ?? 0));

            return Task.FromResult(value);
        }).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_memoryCache.TryGetValue(key, out _));
    }
}