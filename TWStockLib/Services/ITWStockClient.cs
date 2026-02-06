using TWStockLib.Models;
using TWStockLib.Observer;

namespace TWStockLib.Services
{
    public interface ITWStockClient
    {
        Task<StockQuote> GetRealtimeQuoteAsync(string symbol);
        Task<IEnumerable<StockHistory>> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate);
        Task<Dictionary<string, StockData>> GetStockListAsync(MarketType marketType, bool includeWarrant = false);
        Task<Dictionary<string, StockData>> GetAllStockListAsync(bool includeWarrant = false);
        
        // Monitoring
        void SubscribeMonitor(string symbol, IStockPriceObserver observer);
        void UnsubscribeMonitor(string symbol, IStockPriceObserver observer);
    }
}
