using TWStockLib.Factory;
using TWStockLib.Models;
using TWStockLib.Observer;
using TWStockLib.Strategy;

namespace TWStockLib.Services
{
    public class StockMarketService
    {
        private readonly StockPriceSubject _priceSubject;
        private readonly IDataFetchStrategy _dataFetchStrategy;

        public StockMarketService(IStockMarketFactory factory)
        {
            _priceSubject = new StockPriceSubject();
            _dataFetchStrategy = factory.CreateDataFetchStrategy();
        }

        public async Task<StockQuote> GetRealtimeQuote(string symbol, MarketType marketType)
        {
            var quote = await _dataFetchStrategy.FetchRealtimeQuote(symbol, marketType);
            if (quote?.LastPrice.HasValue == true)
            {
                _priceSubject.UpdatePrice(symbol, quote.LastPrice.Value);
            }
            return quote;
        }

        public void SubscribePriceChanges(string symbol, IStockPriceObserver observer)
        {
            _priceSubject.Subscribe(symbol, observer);
        }

        public void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer)
        {
            _priceSubject.Unsubscribe(symbol, observer);
        }

        public async Task<IEnumerable<StockHistory>> GetHistoricalData(
            string symbol, 
            DateTime startDate, 
            DateTime endDate,
            MarketType marketType)
        {
            return await _dataFetchStrategy.FetchHistoricalData(symbol, startDate, endDate, marketType);
        }

        public async Task<Dictionary<string, StockData>> GetStockList(MarketType marketType, bool includeWarrant = false)
        {
            return await _dataFetchStrategy.FetchStockList(marketType, includeWarrant);
        }

        public async Task<Dictionary<string, StockData>> GetAllStockList(bool includeWarrant = false)
        {
            var result = new Dictionary<string, StockData>();
            
            var tseList = await GetStockList(MarketType.TSE, includeWarrant);
            foreach (var item in tseList)
            {
                result[item.Key] = item.Value;
            }
            
            var otcList = await GetStockList(MarketType.OTC, includeWarrant);
            foreach (var item in otcList)
            {
                result[item.Key] = item.Value;
            }
            
            return result;
        }
    }
} 