using Microsoft.Extensions.Caching.Memory;

namespace Omnis.Cache;

public sealed class TwoLevelCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly RedisCacheService _redisCacheService;

    public TwoLevelCacheService(IMemoryCache memoryCache, RedisCacheService redisCacheService)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        ArgumentNullException.ThrowIfNull(redisCacheService);

        _memoryCache = memoryCache;
        _redisCacheService = redisCacheService;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_memoryCache.TryGetValue(key, out T? value))
        {
            return Task.FromResult(value);
        }

        if (TryGetRedisCache(key, out var redisValue))
        {
            _memoryCache.Set(key, redisValue);
            return Task.FromResult((T?)redisValue);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        cancellationToken.ThrowIfCancellationRequested();

        _memoryCache.Set(key, value, options?.ToMemoryCacheEntryOptions() ?? new MemoryCacheEntryOptions());

        TrySetRedisCache(key, value, options, cancellationToken).ConfigureAwait(false);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _memoryCache.Remove(key);

        TryRemoveRedisCache(key, cancellationToken).ConfigureAwait(false);

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CacheOptions, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var options = new CacheOptions();
        var value = await factory(options).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        await SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);

        return value;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_memoryCache.TryGetValue(key, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(TryGetRedisCache(key, out _));
    }

    private bool TryGetRedisCache(string key, out object? value)
    {
        value = default;

        try
        {
            var result = _redisCacheService.GetAsync<object>(key, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            if (result is not null)
            {
                value = result;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task TrySetRedisCache<T>(string key, T value, CacheOptions? options, CancellationToken cancellationToken)
    {
        try
        {
            await _redisCacheService.SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore redis set failures to preserve fallback behavior
        }
    }

    private async Task TryRemoveRedisCache(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _redisCacheService.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore redis remove failures
        }
    }
}
