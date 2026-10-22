using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TWStockLib.Abstractions;

namespace TWStockLib;

/// <summary>
/// 對外門面（Facade）：依 <see cref="MarketType"/> 路由到對應的 <see cref="IStockDataSource"/>，
/// 並把查無資料與上游例外統一收斂為 <see cref="StockResult{T}"/>。
/// 呼叫端取消（<see cref="CancellationToken"/>）時照 .NET 慣例拋出 <see cref="OperationCanceledException"/>；
/// 參數本身不合法（例如空白代號）時拋出 <see cref="ArgumentException"/>。
/// </summary>
public sealed class StockMarketService : IStockMarketService
{
    /// <summary>MIS 約每 5 秒更新一次，追蹤得更頻繁只會增加被上游限流的風險。</summary>
    private static readonly TimeSpan MinimumWatchInterval = TimeSpan.FromSeconds(5);

    /// <summary>判斷市場時依序查詢的清單：先不含權證，再含權證（見 <see cref="ResolveMarketsAsync"/>）。</summary>
    private static readonly StockListOptions[] MarketDetectionPasses = [StockListOptions.Default, StockListOptions.WithWarrants];

    /// <summary>台股代號最長 6 碼（權證、ETF），保留一些餘裕。</summary>
    private const int MaxSymbolLength = 10;

    private readonly IReadOnlyDictionary<MarketType, IStockDataSource> _sources;
    private readonly ILogger<StockMarketService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <param name="sources">各市場的資料來源；同一市場註冊多個時以最後註冊的為準（與 DI 的單一解析慣例一致）。</param>
    /// <param name="logger">記錄上游失敗。</param>
    /// <param name="timeProvider"><c>WatchAsync</c> 輪詢間隔的時間來源；未指定時使用系統時間。</param>
    public StockMarketService(
        IEnumerable<IStockDataSource> sources, ILogger<StockMarketService> logger, TimeProvider? timeProvider = null)
    {
        var byMarket = new Dictionary<MarketType, IStockDataSource>();
        foreach (var source in sources)
            byMarket[source.Market] = source;

        _sources = byMarket;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, QuoteOptions? options = null, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);

        return QueryAsync(market, $"{symbol} 即時報價",
            async source => await source.FetchRealtimeQuoteAsync(symbol, options ?? QuoteOptions.Default, ct), ct);
    }

    public Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        IEnumerable<(string Symbol, MarketType Market)> symbols, QuoteOptions? options = null, CancellationToken ct = default)
        => GetRealtimeQuotesCoreAsync(ValidateTargets(symbols), options ?? QuoteOptions.Default, ct);

    public Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, MarketType market, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);
        if (from > to)
            throw new ArgumentException($"起日 {from:yyyy-MM-dd} 晚於迄日 {to:yyyy-MM-dd}。", nameof(from));

        return QueryAsync(market, $"{symbol} 歷史資料",
            async source => await source.FetchHistoricalDataAsync(symbol, from, to, ct), ct);
    }

    public Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, StockListOptions? options = null, CancellationToken ct = default)
    {
        var listOptions = ValidateStockListOptions(options);

        return QueryAsync(market, "股票清單",
            async source => await source.FetchStockListAsync(listOptions, ct), ct);
    }

    public Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        StockListOptions? options = null, CancellationToken ct = default)
        => GetAllStockListCoreAsync(ValidateStockListOptions(options), ct);

    private static StockListOptions ValidateStockListOptions(StockListOptions? options)
    {
        options ??= StockListOptions.Default;
        if (!Enum.IsDefined(options.Source))
            throw new ArgumentOutOfRangeException(nameof(options), options.Source, $"不支援的清單來源：{options.Source}");
        return options;
    }

    /// <summary>依序查詢每個市場；任一市場失敗就回傳該市場的失敗結果。</summary>
    private async Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListCoreAsync(
        StockListOptions options, CancellationToken ct)
    {
        var merged = new Dictionary<string, StockData>();
        foreach (var market in _sources.Keys)
        {
            var result = await GetStockListAsync(market, options, ct);
            if (!result.IsSuccess)
                return result;

            foreach (var (symbol, stock) in result.Value)
                merged[symbol] = stock;
        }
        return StockResult<IReadOnlyDictionary<string, StockData>>.Ok(merged.ToFrozenDictionary());
    }

    public IAsyncEnumerable<StockQuote> WatchAsync(
        string symbol, MarketType market, TimeSpan? interval = null, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);
        if (!_sources.ContainsKey(market))
            throw new ArgumentException($"不支援的市場：{market}", nameof(market));

        return WatchCoreAsync(symbol, market, ValidateWatchInterval(interval), ct);
    }

    public IAsyncEnumerable<StockQuote> WatchAsync(
        IEnumerable<(string Symbol, MarketType Market)> symbols, TimeSpan? interval = null, CancellationToken ct = default)
    {
        var targets = ValidateTargets(symbols);
        if (targets.Count == 0)
            throw new ArgumentException("至少要追蹤一檔股票。", nameof(symbols));
        foreach (var (symbol, market) in targets)
        {
            if (!_sources.ContainsKey(market))
                throw new ArgumentException($"不支援的市場：{market}（{symbol}）", nameof(symbols));
        }

        return WatchManyCoreAsync(targets, ValidateWatchInterval(interval), ct);
    }

    // ── 不指定市場：依股票清單判斷 ──────────────────────────────────────────────

    public Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, QuoteOptions? options = null, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);

        return WithResolvedMarketAsync(symbol, market => GetRealtimeQuoteAsync(symbol, market, options, ct), ct);
    }

    public Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        IEnumerable<string> symbols, QuoteOptions? options = null, CancellationToken ct = default)
        => GetResolvedQuotesAsync(ValidateSymbols(symbols), options ?? QuoteOptions.Default, ct);

    public Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);
        if (from > to)
            throw new ArgumentException($"起日 {from:yyyy-MM-dd} 晚於迄日 {to:yyyy-MM-dd}。", nameof(from));

        return WithResolvedMarketAsync(symbol, market => GetHistoricalDataAsync(symbol, market, from, to, ct), ct);
    }

    public IAsyncEnumerable<StockQuote> WatchAsync(string symbol, TimeSpan? interval = null, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);

        return WatchResolvedAsync(symbol, ValidateWatchInterval(interval), ct);
    }

    public IAsyncEnumerable<StockQuote> WatchAsync(
        IEnumerable<string> symbols, TimeSpan? interval = null, CancellationToken ct = default)
    {
        var distinct = ValidateSymbols(symbols);
        if (distinct.Count == 0)
            throw new ArgumentException("至少要追蹤一檔股票。", nameof(symbols));

        return WatchManyResolvedAsync(distinct, ValidateWatchInterval(interval), ct);
    }

    private async IAsyncEnumerable<StockQuote> WatchResolvedAsync(
        string symbol, TimeSpan interval, [EnumeratorCancellation] CancellationToken ct)
    {
        var targets = await ResolveForWatchAsync([symbol], interval, ct);
        await foreach (var quote in WatchCoreAsync(symbol, targets[0].Market, interval, ct))
            yield return quote;
    }

    private async IAsyncEnumerable<StockQuote> WatchManyResolvedAsync(
        IReadOnlyList<string> symbols, TimeSpan interval, [EnumeratorCancellation] CancellationToken ct)
    {
        var targets = await ResolveForWatchAsync(symbols, interval, ct);
        await foreach (var quote in WatchManyCoreAsync(targets, interval, ct))
            yield return quote;
    }

    private async IAsyncEnumerable<StockQuote> WatchManyCoreAsync(
        IReadOnlyList<(string Symbol, MarketType Market)> targets, TimeSpan interval, [EnumeratorCancellation] CancellationToken ct)
    {
        var previous = new Dictionary<string, StockQuote>(targets.Count);
        while (true)
        {
            var results = await GetRealtimeQuotesCoreAsync(targets, QuoteOptions.NoCache, ct);

            // 從沒查到過的代號查無資料，多半是代號或市場打錯，不會自己好
            var unknown = targets
                .Where(t => !previous.ContainsKey(t.Symbol) && results[t.Symbol].ErrorCode == StockErrorCodes.NotFound)
                .Select(t => t.Symbol)
                .ToList();
            if (unknown.Count > 0)
                throw new StockQueryException(StockErrorCodes.NotFound, $"查無資料：{string.Join(", ", unknown)} 即時報價");

            foreach (var (symbol, _) in targets)
            {
                if (results[symbol].TryGetValue(out var quote)
                    && (!previous.TryGetValue(symbol, out var last) || quote.LastPrice != last.LastPrice))
                {
                    previous[symbol] = quote;
                    yield return quote;
                }
            }

            // 同一市場的失敗是同一個結果，合併成一筆記錄，不必每檔各記一次
            var failures = targets
                .Where(t => !results[t.Symbol].IsSuccess)
                .GroupBy(t => (results[t.Symbol].ErrorCode, results[t.Symbol].ErrorMessage), t => t.Symbol);
            foreach (var failure in failures)
            {
                _logger.LogWarning("追蹤 {Symbols} 時查詢失敗，{Interval} 後重試：{ErrorCode} {ErrorMessage}",
                    string.Join(", ", failure), interval, failure.Key.ErrorCode, failure.Key.ErrorMessage);
            }

            await Task.Delay(interval, _timeProvider, ct);
        }
    }

    private async IAsyncEnumerable<StockQuote> WatchCoreAsync(
        string symbol, MarketType market, TimeSpan interval, [EnumeratorCancellation] CancellationToken ct)
    {
        StockQuote? previous = null;
        while (true)
        {
            var result = await GetRealtimeQuoteAsync(symbol, market, QuoteOptions.NoCache, ct);
            if (result.IsSuccess)
            {
                if (previous is null || result.Value.LastPrice != previous.LastPrice)
                {
                    previous = result.Value;
                    yield return result.Value;
                }
            }
            else if (result.ErrorCode == StockErrorCodes.NotFound)
            {
                // 代號打錯不會自己好，繼續輪詢只是空轉
                throw new StockQueryException(result.ErrorCode, result.ErrorMessage, result.Exception);
            }
            else
            {
                _logger.LogWarning("追蹤 {Symbol} 時查詢失敗，{Interval} 後重試：{ErrorCode} {ErrorMessage}",
                    symbol, interval, result.ErrorCode, result.ErrorMessage);
            }

            await Task.Delay(interval, _timeProvider, ct);
        }
    }

    /// <summary>依市場分組，每個市場呼叫一次資料來源的批次查詢（各市場同時進行），再把結果攤回每一檔。</summary>
    private async Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesCoreAsync(
        IReadOnlyList<(string Symbol, MarketType Market)> targets, QuoteOptions options, CancellationToken ct)
    {
        var perMarket = await Task.WhenAll(targets
            .GroupBy(t => t.Market, t => t.Symbol)
            .Select(group => QueryMarketQuotesAsync(group.Key, group.ToArray(), options, ct)));

        var results = new Dictionary<string, StockResult<StockQuote>>(targets.Count);
        foreach (var marketResults in perMarket)
        {
            foreach (var (symbol, result) in marketResults)
                results[symbol] = result;
        }
        return results.ToFrozenDictionary();
    }

    private async Task<Dictionary<string, StockResult<StockQuote>>> QueryMarketQuotesAsync(
        MarketType market, string[] symbols, QuoteOptions options, CancellationToken ct)
    {
        var batch = await QueryAsync(market, $"{symbols.Length} 檔即時報價",
            async source => await source.FetchRealtimeQuotesAsync(symbols, options, ct), ct);

        var results = new Dictionary<string, StockResult<StockQuote>>(symbols.Length);
        if (!batch.IsSuccess)
        {
            // 整批失敗：這個市場的每一檔共用同一個失敗結果
            var failure = StockResult<StockQuote>.Fail(batch.ErrorCode, batch.ErrorMessage, batch.Exception);
            foreach (var symbol in symbols)
                results[symbol] = failure;
            return results;
        }

        foreach (var symbol in symbols)
        {
            results[symbol] = batch.Value.TryGetValue(symbol, out var quote)
                ? StockResult<StockQuote>.Ok(quote)
                : StockResult<StockQuote>.Fail(StockErrorCodes.NotFound, $"查無資料：{symbol} 即時報價");
        }
        return results;
    }

    /// <summary>
    /// 依各市場的股票清單（與 <see cref="GetStockListAsync(MarketType, StockListOptions, CancellationToken)"/> 共用快取）判斷每一檔的市場。
    /// 先查不含權證的清單，還找不到的代號再查含權證的清單：內建來源的上市權證清單比其他清單大得多，查股票時用不到。
    /// 每一輪依註冊順序逐一取得清單，全部找到就停，因此只查上市股票時不必下載上櫃清單。
    /// 不在任何清單中的代號為 <c>NOT_FOUND</c>；但有清單取得失敗時，找不到的代號回傳該失敗——它可能就在那份清單裡。
    /// 第一輪失敗的市場第二輪不再重試，交給呼叫端下次再查。
    /// </summary>
    private async Task<Dictionary<string, StockResult<MarketType>>> ResolveMarketsAsync(
        IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var markets = new Dictionary<string, StockResult<MarketType>>(symbols.Count);
        StockResult<MarketType>? listFailure = null;
        var failedMarkets = new HashSet<MarketType>();
        foreach (var options in MarketDetectionPasses)
        {
            foreach (var market in _sources.Keys)
            {
                var pending = symbols.Where(s => !markets.ContainsKey(s)).ToList();
                if (pending.Count == 0)
                    break;
                if (failedMarkets.Contains(market))
                    continue;

                var list = await GetStockListAsync(market, options, ct);
                if (!list.IsSuccess)
                {
                    failedMarkets.Add(market);
                    listFailure ??= StockResult<MarketType>.Fail(list.ErrorCode, list.ErrorMessage, list.Exception);
                    continue;
                }

                foreach (var symbol in pending.Where(list.Value.ContainsKey))
                    markets[symbol] = StockResult<MarketType>.Ok(market);
            }
        }

        foreach (var symbol in symbols.Where(s => !markets.ContainsKey(s)))
        {
            markets[symbol] = listFailure ?? StockResult<MarketType>.Fail(StockErrorCodes.NotFound,
                $"查無資料：{symbol} 不在股票清單中（指數請指定市場）");
        }
        return markets;
    }

    private async Task<StockResult<T>> WithResolvedMarketAsync<T>(
        string symbol, Func<MarketType, Task<StockResult<T>>> query, CancellationToken ct)
    {
        var market = (await ResolveMarketsAsync([symbol], ct))[symbol];
        return market.IsSuccess
            ? await query(market.Value)
            : StockResult<T>.Fail(market.ErrorCode, market.ErrorMessage, market.Exception);
    }

    private async Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetResolvedQuotesAsync(
        IReadOnlyList<string> symbols, QuoteOptions options, CancellationToken ct)
    {
        var markets = await ResolveMarketsAsync(symbols, ct);
        var targets = symbols
            .Where(s => markets[s].IsSuccess)
            .Select(s => (s, markets[s].Value))
            .ToList();
        var quotes = await GetRealtimeQuotesCoreAsync(targets, options, ct);

        var results = new Dictionary<string, StockResult<StockQuote>>(symbols.Count);
        foreach (var symbol in symbols)
        {
            var market = markets[symbol];
            results[symbol] = market.IsSuccess
                ? quotes[symbol]
                : StockResult<StockQuote>.Fail(market.ErrorCode, market.ErrorMessage, market.Exception);
        }
        return results.ToFrozenDictionary();
    }

    /// <summary>
    /// 追蹤前先判斷每一檔的市場：股票清單暫時取得失敗就記錄後隔 <paramref name="interval"/> 重試；
    /// 不在任何清單中的代號不會自己出現，拋出 <c>NOT_FOUND</c>。
    /// </summary>
    private async Task<IReadOnlyList<(string Symbol, MarketType Market)>> ResolveForWatchAsync(
        IReadOnlyList<string> symbols, TimeSpan interval, CancellationToken ct)
    {
        while (true)
        {
            var markets = await ResolveMarketsAsync(symbols, ct);

            var unknown = symbols.Where(s => markets[s].ErrorCode == StockErrorCodes.NotFound).ToList();
            if (unknown.Count > 0)
                throw new StockQueryException(StockErrorCodes.NotFound, $"查無資料：{string.Join(", ", unknown)} 不在股票清單中");

            if (symbols.FirstOrDefault(s => !markets[s].IsSuccess) is not { } failed)
                return symbols.Select(s => (s, markets[s].Value)).ToList();

            _logger.LogWarning("判斷 {Symbol} 等代號的市場時取得股票清單失敗，{Interval} 後重試：{ErrorCode} {ErrorMessage}",
                failed, interval, markets[failed].ErrorCode, markets[failed].ErrorMessage);
            await Task.Delay(interval, _timeProvider, ct);
        }
    }

    /// <summary>檢查每一檔的代號並去除重複，保留傳入的順序。</summary>
    private static IReadOnlyList<string> ValidateSymbols(
        IEnumerable<string> symbols, [CallerArgumentExpression(nameof(symbols))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(symbols, paramName);

        var distinct = new List<string>();
        var seen = new HashSet<string>();
        foreach (var symbol in symbols)
        {
            ValidateSymbol(symbol, paramName);
            if (seen.Add(symbol))
                distinct.Add(symbol);
        }
        return distinct;
    }

    /// <summary>檢查每一檔的代號並去除重複；同一代號指定了不同市場是呼叫端的錯誤。</summary>
    private static IReadOnlyList<(string Symbol, MarketType Market)> ValidateTargets(
        IEnumerable<(string Symbol, MarketType Market)> symbols, [CallerArgumentExpression(nameof(symbols))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(symbols, paramName);

        var markets = new Dictionary<string, MarketType>();
        var targets = new List<(string Symbol, MarketType Market)>();
        foreach (var (symbol, market) in symbols)
        {
            ValidateSymbol(symbol, paramName);
            if (markets.TryAdd(symbol, market))
                targets.Add((symbol, market));
            else if (markets[symbol] != market)
                throw new ArgumentException($"代號 {symbol} 同時指定了 {markets[symbol]} 與 {market} 兩個市場。", paramName);
        }
        return targets;
    }

    private static TimeSpan ValidateWatchInterval(TimeSpan? interval)
    {
        var pollInterval = interval ?? MinimumWatchInterval;
        ArgumentOutOfRangeException.ThrowIfLessThan(pollInterval, MinimumWatchInterval, nameof(interval));
        return pollInterval;
    }

    /// <summary>
    /// 代號只接受 1 到 <see cref="MaxSymbolLength"/> 個英文字母或數字（2330、00631L、指數的 t00…）。
    /// 代號會組進上游 URL：MIS 以「|」分隔多檔，放行任意字元等於讓輸入改寫查詢內容（例如「2330.tw|tse_2317」）。
    /// </summary>
    private static void ValidateSymbol(string symbol, [CallerArgumentExpression(nameof(symbol))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol, paramName);
        if (symbol.Length > MaxSymbolLength || !symbol.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException($"股票代號只能是 1 到 {MaxSymbolLength} 個英文字母或數字：{symbol}", paramName);
    }

    /// <summary>
    /// 依市場找到資料來源並執行查詢：查無資料（<c>null</c>）與上游例外都收斂成失敗結果；
    /// 只有呼叫端自己的取消會照常往外拋。
    /// </summary>
    private async Task<StockResult<T>> QueryAsync<T>(
        MarketType market, string description, Func<IStockDataSource, Task<T?>> query, CancellationToken ct)
        where T : class
    {
        if (!_sources.TryGetValue(market, out var source))
            return StockResult<T>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

        try
        {
            return await query(source) is { } value
                ? StockResult<T>.Ok(value)
                : StockResult<T>.Fail(StockErrorCodes.NotFound, $"查無資料：{description}");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogError(ex, "查詢失敗：{Query} ({Market})", description, market);
            return Failure<T>(ex);
        }
    }

    private static StockResult<T> Failure<T>(Exception ex) => ex switch
    {
        // 不是呼叫端取消的 OperationCanceledException，是 HttpClient 逾時
        OperationCanceledException => StockResult<T>.Fail(StockErrorCodes.Timeout, ex.Message, ex),
        JsonException or FormatException => StockResult<T>.Fail(StockErrorCodes.ParseError, ex.Message, ex),
        _ => StockResult<T>.Fail(StockErrorCodes.UpstreamError, ex.Message, ex),
    };
}
