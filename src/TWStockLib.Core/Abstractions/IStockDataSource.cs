using TWStockLib.Models;

namespace TWStockLib.Abstractions;

/// <summary>
/// 單一市場的股票資料來源。每個市場（TSE / OTC / 未來的興櫃…）各自實作一份，
/// 由 <c>StockMarketService</c> 依 <see cref="Market"/> 路由——新增市場零修改現有程式碼（OCP）。
/// </summary>
public interface IStockDataSource
{
    /// <summary>此來源負責的市場別。</summary>
    MarketType Market { get; }

    /// <summary>取得即時報價；查無資料回 <c>null</c>。</summary>
    Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// 取得即時報價；<paramref name="bypassCache"/> 為 <c>true</c> 時不使用來源內部的快取，一律向上游查詢（並更新快取）。
    /// 預設實作忽略此參數、等同上一個方法；內部有快取的來源應覆寫。
    /// </summary>
    Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, bool bypassCache, CancellationToken ct = default)
        => FetchRealtimeQuoteAsync(symbol, ct);

    /// <summary>取得日期區間內的歷史資料。</summary>
    Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>取得此市場的股票清單。</summary>
    Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);
}
