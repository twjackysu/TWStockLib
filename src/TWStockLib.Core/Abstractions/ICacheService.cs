using System.Diagnostics.CodeAnalysis;

namespace TWStockLib.Abstractions;

/// <summary>
/// 資料來源使用的快取。<c>null</c> 代表查無資料，不會被快取。
/// <c>factory</c> 收到的 <see cref="CancellationToken"/> 由實作決定：可直接傳入呼叫端的 <c>ct</c>，
/// 或在合併多個呼叫端時改傳共用的 token（見 <see cref="GetOrSetAsync{T}"/>）。
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 有快取就直接回傳；否則執行 <paramref name="factory"/>，並把非 <c>null</c> 的結果快取 <paramref name="expiry"/>。
    /// 實作可合併同一個 key 同時發生的未命中（single-flight）：只執行一次 <paramref name="factory"/>、結果交給所有等候者；
    /// 某個等候者的 <paramref name="ct"/> 取消只會讓它自己離開，所有等候者都離開時才取消傳給 <paramref name="factory"/> 的 token。
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// 一律執行 <paramref name="factory"/>，並以結果覆寫快取；結果為 <c>null</c> 時保留原本的快取值，
    /// 避免一次查無資料就把先前取得的有效值洗掉。不與 <see cref="GetOrSetAsync{T}"/> 進行中的載入合併。
    /// </summary>
    Task<T> RefreshAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// 只查快取、不載入：有未過期的值時回傳 <c>true</c>。不會加入或發起 <see cref="GetOrSetAsync{T}"/> 的載入，
    /// 供批次查詢先挑出已快取的項目，其餘再合併成一次上游查詢。
    /// </summary>
    bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value);
}
