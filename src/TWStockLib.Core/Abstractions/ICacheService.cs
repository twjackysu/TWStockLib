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
}
