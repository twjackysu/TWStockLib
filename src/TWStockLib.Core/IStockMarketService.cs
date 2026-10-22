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

    /// <summary>
    /// 取得指定市場的股票清單，從預設的 <see cref="StockListSource.OpenApi"/> 取得。
    /// 要改用最完整但較慢的 ISIN 頁面時，用 <see cref="StockListOptions"/> 的多載。
    /// </summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>
    /// 以 <paramref name="options"/> 取得指定市場的股票清單，例如改用最完整但較慢的 ISIN 頁面：
    /// <c>GetStockListAsync(MarketType.TSE, new StockListOptions { Source = StockListSource.Isin })</c>。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 為 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="StockListOptions.Source"/> 不是定義的值。</exception>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, StockListOptions options, CancellationToken ct = default);

    /// <summary>取得上市 + 上櫃的股票清單，從預設的 <see cref="StockListSource.OpenApi"/> 取得。</summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>以 <paramref name="options"/> 取得上市 + 上櫃的股票清單。</summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 為 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="StockListOptions.Source"/> 不是定義的值。</exception>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        StockListOptions options, CancellationToken ct = default);

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

    /// <summary>
    /// 同時追蹤多檔股票的成交價：每一輪以 <see cref="GetRealtimeQuotesAsync(IEnumerable{ValueTuple{string, MarketType}}, QuoteOptions?, CancellationToken)"/>
    /// 略過快取一次查完所有代號（同一市場合併查詢，內建來源每 50 檔一個 MIS 請求），每一檔第一次查到時回傳一次，
    /// 之後只回傳成交價有變動的那幾檔，同一輪內依傳入的順序。間隔同單檔多載（預設與最短皆為 5 秒）。
    /// 上游暫時失敗會記錄後在下一輪重試；還沒查到過的代號回 <c>NOT_FOUND</c> 時拋出 <see cref="StockQueryException"/>
    /// （多半是代號或市場打錯），已經查到過的代號之後查無資料只記錄、不中斷其他代號的追蹤；
    /// 取消時拋出 <see cref="OperationCanceledException"/> 結束列舉。
    /// </summary>
    /// <exception cref="ArgumentException">沒有代號、代號格式不合法、同一代號指定兩個市場、不支援的市場，或間隔短於 5 秒。</exception>
    /// <example>
    /// <code>
    /// await foreach (var quote in stock.WatchAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC)], ct: stoppingToken))
    ///     Console.WriteLine($"{quote.Symbol} {quote.LastPrice}");
    /// </code>
    /// </example>
    IAsyncEnumerable<StockQuote> WatchAsync(
        IEnumerable<(string Symbol, MarketType Market)> symbols, TimeSpan? interval = null, CancellationToken ct = default);

    // ── 不指定市場 ───────────────────────────────────────────────────────────
    // 以下多載依完整股票清單（GetStockListAsync 含權證；內建來源含不含權證共用同一份快取）判斷代號屬於上市或上櫃，再呼叫指定市場的版本。
    // 第一次判斷需要下載股票清單（每個市場一頁 ISIN 網頁，上市約 9.7 MB，實測 12～78 秒，且受每主機限流），
    // 之後快取 TwStockOptions.StockListCacheDuration（預設 1 天）；依註冊順序找到就停，上市股票只需要上市清單。
    // 不在清單中的代號（例如打錯、指數 t00）回傳 NOT_FOUND，這類代號請改用指定市場的多載。

    /// <summary>
    /// 不指定市場取得即時報價：依股票清單判斷上市或上櫃。第一次呼叫需下載股票清單（上市約 9.7 MB，實測 12～78 秒），之後快取 1 天；
    /// 啟動時可先呼叫 <see cref="GetAllStockListAsync"/> 預熱。不在清單中的代號（含指數 t00）回傳 <c>NOT_FOUND</c>；
    /// 股票清單取得失敗時回傳該失敗。
    /// </summary>
    Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, QuoteOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 不指定市場一次取得多檔即時報價：依股票清單判斷每一檔的市場後，同
    /// <see cref="GetRealtimeQuotesAsync(IEnumerable{ValueTuple{string, MarketType}}, QuoteOptions?, CancellationToken)"/>。
    /// 不在清單中的代號為 <c>NOT_FOUND</c>。第一次呼叫的成本見
    /// <see cref="GetRealtimeQuoteAsync(string, QuoteOptions?, CancellationToken)"/>。
    /// </summary>
    /// <exception cref="ArgumentException">代號格式不合法。</exception>
    Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        IEnumerable<string> symbols, QuoteOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// 不指定市場取得歷史資料：依股票清單判斷上市或上櫃。第一次呼叫的成本見
    /// <see cref="GetRealtimeQuoteAsync(string, QuoteOptions?, CancellationToken)"/>。
    /// </summary>
    Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// 不指定市場追蹤成交價：開始時依股票清單判斷上市或上櫃（清單暫時取得失敗會在下一輪重試，
    /// 不在清單中拋出 <c>NOT_FOUND</c> 的 <see cref="StockQueryException"/>），之後同
    /// <see cref="WatchAsync(string, MarketType, TimeSpan?, CancellationToken)"/>。
    /// </summary>
    IAsyncEnumerable<StockQuote> WatchAsync(string symbol, TimeSpan? interval = null, CancellationToken ct = default);

    /// <summary>
    /// 不指定市場追蹤多檔成交價：開始時依股票清單判斷每一檔的市場（清單暫時取得失敗會在下一輪重試，
    /// 任一檔不在清單中拋出 <c>NOT_FOUND</c> 的 <see cref="StockQueryException"/>），之後同
    /// <see cref="WatchAsync(IEnumerable{ValueTuple{string, MarketType}}, TimeSpan?, CancellationToken)"/>。
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var quote in stock.WatchAsync(["2330", "6510", "0050"], ct: stoppingToken))
    ///     Console.WriteLine($"{quote.Symbol} {quote.Market} {quote.LastPrice}");
    /// </code>
    /// </example>
    IAsyncEnumerable<StockQuote> WatchAsync(
        IEnumerable<string> symbols, TimeSpan? interval = null, CancellationToken ct = default);
}
