using Microsoft.Extensions.DependencyInjection;

namespace Omnis.Cache;

public static class CacheExtensions
{
    extension(IServiceCollection services)
    {
        public void AddMemoryCache()
        {
            services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        public void AddRedisCache()
        {
            throw new NotImplementedException();
        }
    }
}