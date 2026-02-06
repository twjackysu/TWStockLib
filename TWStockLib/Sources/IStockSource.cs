using TWStockLib.Models;

namespace TWStockLib.Sources
{
    public interface IStockSource
    {
        MarketType Market { get; }
        Task<StockQuote> FetchRealtimeQuoteAsync(string symbol);
        Task<IEnumerable<StockHistory>> FetchHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate);
        Task<Dictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false);
    }
}
