namespace TWStockLib.Abstractions;

/// <summary>
/// 單一市場的股票資料來源。每個市場（TSE / OTC / 未來的興櫃…）各自實作一份，
/// 由 <c>StockMarketService</c> 依 <see cref="Market"/> 路由——新增市場零修改現有程式碼（OCP）。
/// 回傳的模型與集合會直接交給呼叫端、也可能放進快取共用，實作應回傳不可變的集合。
/// </summary>
public interface IStockDataSource
{
    /// <summary>此來源負責的市場別。</summary>
    MarketType Market { get; }

    /// <summary>取得即時報價；查無資料回 <c>null</c>。內部有快取的來源應遵守 <see cref="QuoteOptions.BypassCache"/>。</summary>
    Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default);

    /// <summary>取得 <paramref name="from"/> 到 <paramref name="to"/>（含）之間的歷史資料，依日期排序。</summary>
    Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// 取得此市場的股票清單；<paramref name="includeWarrant"/> 為 <c>true</c> 時即完整清單。
    /// 實作應填入 <see cref="StockData.SecurityType"/>，呼叫端才能依證券類型過濾（未填的項目為
    /// <see cref="SecurityType.Unknown"/>，只會出現在不指定類型的查詢中）。內建來源只下載一次完整清單，
    /// 不含權證的版本在讀取時過濾。
    /// </summary>
    Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);
}
