using TWStockLib.Models;
using TWStockLib.Observer;

namespace TWStockLib.Services
{
    /// <summary>
    /// 台灣股市資料服務的對外介面：即時報價、歷史資料、股票清單與價格變動訂閱。
    /// </summary>
    public interface IStockMarketService
    {
        /// <summary>取得指定股票的即時報價。</summary>
        Task<StockQuote> GetRealtimeQuote(string symbol, MarketType marketType);

        /// <summary>取得指定股票在日期區間內的歷史資料（以月為單位向上游查詢）。</summary>
        Task<IEnumerable<StockHistory>> GetHistoricalData(
            string symbol, DateTime startDate, DateTime endDate, MarketType marketType);

        /// <summary>取得指定市場的股票清單。</summary>
        Task<Dictionary<string, StockData>> GetStockList(MarketType marketType, bool includeWarrant = false);

        /// <summary>取得上市 + 上櫃的完整股票清單。</summary>
        Task<Dictionary<string, StockData>> GetAllStockList(bool includeWarrant = false);

        /// <summary>訂閱指定股票的價格變動通知。</summary>
        void SubscribePriceChanges(string symbol, IStockPriceObserver observer);

        /// <summary>取消訂閱指定股票的價格變動通知。</summary>
        void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer);
    }
}
