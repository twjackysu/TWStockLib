using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using TWStockLib.Abstractions;
using TWStockLib.Models;
using TWStockLib.Observer;

namespace TWStockLib.Services;

/// <summary>
/// 對外門面（Facade）：依 <see cref="MarketType"/> 路由到對應的 <see cref="IStockDataSource"/>，
/// 統一例外處理為 <see cref="StockResult{T}"/>，並在報價更新時通知價格觀察者。
/// 呼叫端取消（<see cref="CancellationToken"/>）時照 .NET 慣例拋出 <see cref="OperationCanceledException"/>。
/// </summary>
public class StockMarketService : IStockMarketService
{
    private readonly StockPriceSubject _priceSubject = new();
    private readonly IReadOnlyDictionary<MarketType, IStockDataSource> _sources;
    private readonly ILogger<StockMarketService> _logger;

    /// <param name="sources">各市場的資料來源；同一市場註冊多個時以最後註冊的為準（與 DI 的單一解析慣例一致）。</param>
    /// <param name="logger">記錄上游失敗。</param>
    public StockMarketService(IEnumerable<IStockDataSource> sources, ILogger<StockMarketService> logger)
    {
        var byMarket = new Dictionary<MarketType, IStockDataSource>();
        foreach (var source in sources)
            byMarket[source.Market] = source;

        _sources = byMarket;
        _logger = logger;
    }

    public async Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, QuoteOptions? options = null, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(market, out var source))
            return StockResult<StockQuote>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

        StockQuote? quote;
        try
        {
            quote = await source.FetchRealtimeQuoteAsync(symbol, options ?? QuoteOptions.Default, ct);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "取得即時報價失敗：{Symbol} ({Market})", symbol, market);
            return Failure<StockQuote>(ex);
        }

        if (quote is null)
            return StockResult<StockQuote>.Fail(StockErrorCodes.NotFound, $"查無報價：{symbol}");

        if (quote.LastPrice.HasValue)
            NotifyPriceObservers(symbol, quote.LastPrice.Value);

        return StockResult<StockQuote>.Ok(quote);
    }

    /// <summary>觀察者的例外屬於呼叫端自己的程式，只記錄、不影響已成功取得的報價。</summary>
    private void NotifyPriceObservers(string symbol, decimal price)
    {
        try
        {
            _priceSubject.UpdatePrice(symbol, price);
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "價格觀察者處理 {Symbol} 的價格變動時發生例外", symbol);
        }
    }

    public async Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, MarketType market, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(market, out var source))
            return StockResult<IReadOnlyList<StockHistory>>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

        try
        {
            var history = await source.FetchHistoricalDataAsync(symbol, from, to, ct);
            return StockResult<IReadOnlyList<StockHistory>>.Ok(history);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "取得歷史資料失敗：{Symbol} ({Market})", symbol, market);
            return Failure<IReadOnlyList<StockHistory>>(ex);
        }
    }

    public async Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(market, out var source))
            return StockResult<IReadOnlyDictionary<string, StockData>>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

        try
        {
            var list = await source.FetchStockListAsync(includeWarrant, ct);
            return StockResult<IReadOnlyDictionary<string, StockData>>.Ok(list);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "取得股票清單失敗：{Market}", market);
            return Failure<IReadOnlyDictionary<string, StockData>>(ex);
        }
    }

    public async Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default)
    {
        try
        {
            var merged = new Dictionary<string, StockData>();
            foreach (var source in _sources.Values)
            {
                var list = await source.FetchStockListAsync(includeWarrant, ct);
                foreach (var kvp in list)
                    merged[kvp.Key] = kvp.Value;
            }
            return StockResult<IReadOnlyDictionary<string, StockData>>.Ok(merged.ToFrozenDictionary());
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "取得完整股票清單失敗");
            return Failure<IReadOnlyDictionary<string, StockData>>(ex);
        }
    }

    public void SubscribePriceChanges(string symbol, IStockPriceObserver observer)
        => _priceSubject.Subscribe(symbol, observer);

    public void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer)
        => _priceSubject.Unsubscribe(symbol, observer);

    /// <summary>呼叫端自己取消的，不收斂成失敗結果，讓 <see cref="OperationCanceledException"/> 照常往外拋。</summary>
    private static bool IsCallerCancellation(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;

    /// <summary>不是呼叫端取消的 <see cref="OperationCanceledException"/>，是 HttpClient 逾時。</summary>
    private static StockResult<T> Failure<T>(Exception ex)
        => ex is OperationCanceledException
            ? StockResult<T>.Fail(StockErrorCodes.Timeout, ex.Message)
            : StockResult<T>.Fail(StockErrorCodes.UpstreamError, ex.Message);
}
