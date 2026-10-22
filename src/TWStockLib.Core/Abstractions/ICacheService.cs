namespace TWStockLib.Abstractions;

/// <summary>資料來源使用的快取。<c>null</c> 代表查無資料，不會被快取。</summary>
public interface ICacheService
{
    /// <summary>有快取就直接回傳；否則執行 <paramref name="factory"/>，並把結果快取 <paramref name="expiry"/>。</summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry);

    /// <summary>
    /// 一律執行 <paramref name="factory"/>，並以結果覆寫快取；結果為 <c>null</c> 時保留原本的快取值，
    /// 避免一次查無資料就把先前取得的有效值洗掉。
    /// </summary>
    Task<T> RefreshAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry);

    /// <summary>
    /// 同 <see cref="GetOrSetAsync{T}(string, Func{Task{T}}, TimeSpan)"/>，但 <paramref name="factory"/> 會收到
    /// <see cref="CancellationToken"/>，實作可藉此合併同一個 key 同時發生的未命中（single-flight）：
    /// 只執行一次 <paramref name="factory"/>、結果交給所有等候者；某個等候者的 <paramref name="ct"/> 取消只會讓它自己離開，
    /// 所有等候者都離開時才取消傳給 <paramref name="factory"/> 的 token。
    /// 預設實作不合併，直接以 <paramref name="ct"/> 呼叫 <paramref name="factory"/>。
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct)
        => GetOrSetAsync(key, () => factory(ct), expiry);
}
