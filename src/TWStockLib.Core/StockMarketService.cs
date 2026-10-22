using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TWStockLib.Abstractions;
using TWStockLib.Models;

namespace TWStockLib.Services;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        return QueryAsync(market, $"{symbol} 即時報價",
            async source => await source.FetchRealtimeQuoteAsync(symbol, options ?? QuoteOptions.Default, ct), ct);
    }

    public Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, MarketType market, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
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
