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
    /// 一次取得多檔即時報價。同一市場的代號合併查詢（內建來源每 50 檔一個 MIS 請求），比逐檔呼叫
    /// <see cref="GetRealtimeQuoteAsync(string, MarketType, QuoteOptions?, CancellationToken)"/> 省下大量請求；
    /// 快取以每一檔為單位，與單檔查詢共用，已快取的代號不會再查上游（<see cref="QuoteOptions.NoCache"/> 除外）。
    /// </summary>
    /// <returns>
    /// 以代號為 key、每個傳入的代號恰好一筆的字典（重複的代號只查一次），每一檔各自成功或失敗：
    /// 查無此代號、或代號不屬於指定的市場時為 <c>NOT_FOUND</c>；不支援的市場為 <c>SOURCE_NOT_FOUND</c>；
    /// 上游失敗時，同一市場這次查詢的每一檔都是同一個失敗結果（<c>UPSTREAM_ERROR</c> / <c>TIMEOUT</c> / <c>PARSE_ERROR</c>）。
    /// 字典不保證順序，請以自己的代號清單逐一取值。
    /// </returns>
    /// <exception cref="ArgumentException">代號格式不合法，或同一代號指定了兩個不同的市場。</exception>
    /// <example>
    /// <code>
    /// var quotes = await stock.GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC)]);
    /// if (quotes["2330"].TryGetValue(out var tsmc))
    ///     Console.WriteLine(tsmc.LastPrice);
    /// </code>
    /// </example>
    Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        IEnumerable<(string Symbol, MarketType Market)> symbols, QuoteOptions? options = null, CancellationToken ct = default);

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
