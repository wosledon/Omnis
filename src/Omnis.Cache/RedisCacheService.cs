using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Omnis.Cache;

public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly int _database;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> redisOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(redisOptions);

        _connectionMultiplexer = connectionMultiplexer;
        _database = redisOptions.Value.Database;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        return database.StringGetAsync(BuildKey(key)).ContinueWith(task =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = task.Result;

            return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString()!, _jsonSerializerOptions) : default;
        }, cancellationToken);
    }

    public Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        var serializedValue = JsonSerializer.Serialize(value, _jsonSerializerOptions);

        return database.StringSetAsync(BuildKey(key), serializedValue, GetExpiry(options), When.Always, CommandFlags.None);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        return database.KeyDeleteAsync(BuildKey(key), CommandFlags.None);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CacheOptions, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var options = new CacheOptions();
        var value = await factory(options).ConfigureAwait(false);

        if (value is not null)
        {
            await SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
        }

        return value;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        return database.KeyExistsAsync(BuildKey(key), CommandFlags.None);
    }

    private IDatabase GetDatabase() => _connectionMultiplexer.GetDatabase(_database);

    private static string BuildKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("缓存键不能为空。", nameof(key));
        }

        return key;
    }

    private static TimeSpan? GetExpiry(CacheOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        if (options.AbsoluteExpirationInSeconds is > 0 or double.PositiveInfinity or double.NegativeInfinity)
        {
            return TimeSpan.FromSeconds(options.AbsoluteExpirationInSeconds!.Value);
        }

        if (options.AbsoluteExpirationRelativeToNow is { } absolute)
        {
            return absolute;
        }

        if (options.SlidingExpirationInSeconds is > 0 or double.PositiveInfinity or double.NegativeInfinity)
        {
            return TimeSpan.FromSeconds(options.SlidingExpirationInSeconds!.Value);
        }

        if (options.SlidingExpiration is { } sliding)
        {
            return sliding;
        }

        return null;
    }
}

public static class CacheEntryOptionsExtensions
{
    public static CacheOptions? ToCacheOptions(this MemoryCacheEntryOptions options)
    {
        if (options is null)
        {
            return null;
        }

        var cacheOptions = new CacheOptions();

        if (options.AbsoluteExpirationRelativeToNow is { } absolute)
        {
            cacheOptions.AbsoluteExpirationRelativeToNow = absolute;
        }

        if (options.SlidingExpiration is { } sliding)
        {
            cacheOptions.SlidingExpiration = sliding;
        }

        return cacheOptions;
    }
}