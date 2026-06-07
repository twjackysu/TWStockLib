using TWStockLib.Models;
using TWStockLib.Observer;

namespace TWStockLib.Services;

/// <summary>
/// 台灣股市資料服務的對外介面：即時報價、歷史資料、股票清單與價格變動訂閱。
/// 所有查詢均為非同步、可取消，並以 <see cref="StockResult{T}"/> 統一回傳成功/失敗。
/// </summary>
public interface IStockMarketService
{
    /// <summary>取得指定股票的即時報價。</summary>
    Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, CancellationToken ct = default);

    /// <summary>取得指定股票在日期區間內的歷史資料（以月為單位向上游查詢）。</summary>
    Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, DateTime startDate, DateTime endDate, MarketType market,
        CancellationToken ct = default);

    /// <summary>取得指定市場的股票清單。</summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>取得上市 + 上櫃的完整股票清單。</summary>
    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);

    /// <summary>訂閱指定股票的價格變動通知。</summary>
    void SubscribePriceChanges(string symbol, IStockPriceObserver observer);

    /// <summary>取消訂閱指定股票的價格變動通知。</summary>
    void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer);
}
