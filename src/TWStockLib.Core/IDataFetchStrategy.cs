using TWStockLib.Models;

namespace TWStockLib.Strategy
{
    public interface IDataFetchStrategy
    {
        Task<StockQuote> FetchRealtimeQuote(string symbol, MarketType marketType);
        Task<IEnumerable<StockHistory>> FetchHistoricalData(string symbol, DateTime startDate, DateTime endDate, MarketType marketType);
        Task<Dictionary<string, StockData>> FetchStockList(MarketType marketType, bool includeWarrant = false);
    }
} 