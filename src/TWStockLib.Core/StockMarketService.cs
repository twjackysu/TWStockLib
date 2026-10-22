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

    /// <summary>台股代號最長 6 碼（權證、ETF），保留一些餘裕。</summary>
    private const int MaxSymbolLength = 10;

    private readonly IReadOnlyDictionary<MarketType, IStockDataSource> _sources;
    private readonly ILogger<StockMarketService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <param name="sources">各市場的資料來源；同一市場註冊多個時以最後註冊的為準（與 DI 的單一解析慣例一致）。</param>
    /// <param name="logger">記錄上游失敗。</param>
    /// <param name="timeProvider"><see cref="WatchAsync"/> 輪詢間隔的時間來源；未指定時使用系統時間。</param>
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
        MarketType market, bool includeWarrant = false, CancellationToken ct = default)
        => QueryAsync(market, "股票清單",
            async source => await source.FetchStockListAsync(includeWarrant, ct), ct);

    /// <summary>依序查詢每個市場；任一市場失敗就回傳該市場的失敗結果。</summary>
    public async Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default)
    {
        var merged = new Dictionary<string, StockData>();
        foreach (var market in _sources.Keys)
        {
            var result = await GetStockListAsync(market, includeWarrant, ct);
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
        var pollInterval = interval ?? MinimumWatchInterval;
        ArgumentOutOfRangeException.ThrowIfLessThan(pollInterval, MinimumWatchInterval, nameof(interval));

        return WatchCoreAsync(symbol, market, pollInterval, ct);
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
        foreach (var symbol in symbols)
        {
            if (!batch.IsSuccess)
                results[symbol] = StockResult<StockQuote>.Fail(batch.ErrorCode, batch.ErrorMessage, batch.Exception);
            else if (batch.Value.TryGetValue(symbol, out var quote))
                results[symbol] = StockResult<StockQuote>.Ok(quote);
            else
                results[symbol] = StockResult<StockQuote>.Fail(StockErrorCodes.NotFound, $"查無資料：{symbol} 即時報價");
        }
        return results;
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
