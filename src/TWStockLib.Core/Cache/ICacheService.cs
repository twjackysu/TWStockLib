namespace TWStockLib.Cache;

public interface ICacheService
{
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);

    /// <summary>
    /// 同上；<paramref name="bypassCache"/> 為 <c>true</c> 時略過既有的快取內容，一律執行 <paramref name="factory"/>。
    /// 預設實作只做到「略過」，不會把結果寫回快取；有實際快取的實作應覆寫，讓略過之後的一般呼叫拿到較新的值。
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry, bool bypassCache)
        => bypassCache ? factory() : GetOrSetAsync(key, factory, expiry);
}
