namespace TWStockLib;

/// <summary>
/// 台灣股市資料服務的對外介面：即時報價、歷史資料、股票清單與成交價追蹤。
/// 所有查詢均為非同步、可取消，並以 <see cref="StockResult{T}"/> 統一回傳成功/失敗。
/// 回傳的模型（record）與集合都是不可變的，可以放心共用。
/// </summary>
public interface IStockMarketService
{
    /// <summary>
    /// 取得指定股票的即時報價。預設使用 30 秒的內部快取；需要最新值時傳 <see cref="QuoteOptions.NoCache"/>。
    /// 只想傳 <see cref="CancellationToken"/> 時可用 <see cref="StockMarketServiceExtensions"/> 的多載。
    /// </summary>
    Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, QuoteOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 取得指定股票在 <paramref name="from"/> 到 <paramref name="to"/>（含）之間的歷史資料，依日期排序。
    /// 以月為單位向上游查詢。
    /// </summary>
    Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, MarketType market, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>取得指定市場的股票清單。</summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>取得上市 + 上櫃的完整股票清單。</summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>
    /// 持續追蹤指定股票的成交價：先回傳一次目前的報價，之後每隔 <paramref name="interval"/>
    /// （預設與最短皆為 5 秒，MIS 約每 5 秒更新一次）略過快取查詢，只在成交價變動時回傳新報價。
    /// 上游暫時失敗會記錄後在下一輪重試；查無此股票時拋出 <see cref="StockQueryException"/>；
    /// 取消時照 .NET 慣例拋出 <see cref="OperationCanceledException"/> 結束列舉。
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var quote in stock.WatchAsync("2330", MarketType.TSE, ct: stoppingToken))
    ///     Console.WriteLine($"{quote.Symbol} {quote.LastPrice}");
    /// </code>
    /// </example>
    IAsyncEnumerable<StockQuote> WatchAsync(
        string symbol, MarketType market, TimeSpan? interval = null, CancellationToken ct = default);
}
