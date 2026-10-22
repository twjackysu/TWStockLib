using Microsoft.Extensions.Caching.Memory;

namespace TWStockLib.Cache;

/// <summary>以 <see cref="IMemoryCache"/> 實作的 <see cref="ICacheService"/>。</summary>
public sealed class MemoryCacheService(IMemoryCache memoryCache) : ICacheService
{
    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry)
    {
        if (memoryCache.TryGetValue(key, out T? cachedValue) && cachedValue is not null)
            return cachedValue;

        return await RefreshAsync(key, factory, expiry);
    }

    public async Task<T> RefreshAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry)
    {
        var value = await factory();
        if (value is not null)
            memoryCache.Set(key, value, expiry);

        return value;
    }
}
