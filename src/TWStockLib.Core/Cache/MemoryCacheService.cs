using Microsoft.Extensions.Caching.Memory;

namespace TWStockLib.Cache
{
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _memoryCache;

        public MemoryCacheService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            if (_memoryCache.TryGetValue(key, out T cachedValue))
            {
                return cachedValue;
            }

            var value = await factory();

            var cacheEntryOptions = new MemoryCacheEntryOptions();
            if (expiry.HasValue)
            {
                cacheEntryOptions.SetAbsoluteExpiration(expiry.Value);
            }
            else
            {
                cacheEntryOptions.SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
            }

            _memoryCache.Set(key, value, cacheEntryOptions);

            return value;
        }
    }
} 