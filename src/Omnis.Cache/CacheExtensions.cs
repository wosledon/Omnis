using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Omnis.Cache;

public static class CacheExtensions
{
    extension(IServiceCollection services)
    {
        public void AddMemoryCache()
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        public void AddRedisCache(RedisOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

            services.AddSingleton(options);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.ConnectionString));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }

        public void AddTwoLevelCache(RedisOptions? options = null)
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheService>(sp =>
            {
                var memoryCache = sp.GetRequiredService<IMemoryCache>();
                var redisService = TryCreateRedisService(options);

                return redisService is null
                    ? new MemoryCacheService(memoryCache)
                    : new TwoLevelCacheService(memoryCache, (RedisCacheService)redisService);
            });
        }
    }

    private static ICacheService? TryCreateRedisService(RedisOptions? options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return null;
        }

        try
        {
            var multiplexer = ConnectionMultiplexer.Connect(options.ConnectionString);
            return new RedisCacheService(multiplexer, Options.Create(options));
        }
        catch
        {
            return null;
        }
    }
}
