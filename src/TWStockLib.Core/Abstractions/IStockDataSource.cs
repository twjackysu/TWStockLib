using System.Collections.Frozen;

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

    /// <summary>
    /// 一次取得多檔即時報價（<paramref name="symbols"/> 不重複、格式已檢查過），回傳以傳入的代號為 key 的字典；
    /// 查無資料的代號不放進結果。任何一次上游查詢失敗就拋出例外，整批視為失敗。
    /// 內部有快取的來源應遵守 <see cref="QuoteOptions.BypassCache"/>，並以每一檔為單位快取。
    /// </summary>
    /// <remarks>
    /// 預設實作逐檔呼叫 <see cref="FetchRealtimeQuoteAsync"/>，自訂來源可以不實作；
    /// 上游支援一次查多檔的來源應覆寫，以減少請求數。
    /// </remarks>
    async Task<IReadOnlyDictionary<string, StockQuote>> FetchRealtimeQuotesAsync(
        IReadOnlyList<string> symbols, QuoteOptions options, CancellationToken ct = default)
    {
        var quotes = new Dictionary<string, StockQuote>(symbols.Count);
        foreach (var symbol in symbols)
        {
            if (await FetchRealtimeQuoteAsync(symbol, options, ct) is { } quote)
                quotes[symbol] = quote;
        }
        return quotes.ToFrozenDictionary();
    }

    /// <summary>取得 <paramref name="from"/> 到 <paramref name="to"/>（含）之間的歷史資料，依日期排序。</summary>
    Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// 取得此市場的股票清單；<see cref="StockListOptions.IncludeWarrant"/> 為 <c>true</c> 時含權證（含牛熊證）。
    /// 只有單一上游的來源可以不理會 <see cref="StockListOptions.Source"/>。
    /// </summary>
    Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        StockListOptions options, CancellationToken ct = default);
}
