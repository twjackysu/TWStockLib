using Microsoft.Extensions.Caching.Memory;

namespace TWStockLib.Cache;

public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;

    public MemoryCacheService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        => GetOrSetAsync(key, factory, expiry, bypassCache: false);

    /// <summary>
    /// 略過快取時一律執行 <paramref name="factory"/>，並以新值覆寫快取；
    /// 但 factory 回傳 <c>null</c>（查無資料）時不覆寫，避免把先前取得的有效值洗掉。
    /// </summary>
    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry, bool bypassCache)
    {
        if (!bypassCache && _memoryCache.TryGetValue(key, out T? cachedValue) && cachedValue is not null)
        {
            return cachedValue;
        }

        var value = await factory();

        if (bypassCache && value is null)
        {
            return value;
        }

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
