using TWStockLib.Models;
using TWStockLib.Observer;
using TWStockLib.Sources;

namespace TWStockLib.Services
{
    public class TWStockClient : ITWStockClient
    {
        private readonly IEnumerable<IStockSource> _sources;
        private readonly StockPriceSubject _priceSubject;
        private Dictionary<string, StockData> _stockCache;

        public TWStockClient(IEnumerable<IStockSource> sources)
        {
            _sources = sources;
            _priceSubject = new StockPriceSubject();
            _stockCache = new Dictionary<string, StockData>();
        }

        private IStockSource GetSource(MarketType marketType)
        {
            return _sources.FirstOrDefault(s => s.Market == marketType) 
                   ?? throw new ArgumentException($"No source found for market {marketType}");
        }

        private async Task<IStockSource> ResolveSourceAsync(string symbol)
        {
            if (_stockCache.TryGetValue(symbol, out var stock))
            {
                return GetSource(stock.Market);
            }

            if (_stockCache.Count == 0)
            {
                await GetAllStockListAsync();
                if (_stockCache.TryGetValue(symbol, out stock))
                {
                    return GetSource(stock.Market);
                }
            }

            return GetSource(MarketType.TSE);
        }

        public async Task<StockQuote> GetRealtimeQuoteAsync(string symbol)
        {
            var source = await ResolveSourceAsync(symbol);
            var quote = await source.FetchRealtimeQuoteAsync(symbol);
            
            if (quote == null && source.Market == MarketType.TSE)
            {
                var otcSource = GetSource(MarketType.OTC);
                quote = await otcSource.FetchRealtimeQuoteAsync(symbol);
            }

            if (quote?.LastPrice.HasValue == true)
            {
                _priceSubject.UpdatePrice(symbol, quote.LastPrice.Value);
            }
            return quote;
        }

        public async Task<IEnumerable<StockHistory>> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            var source = await ResolveSourceAsync(symbol);
            return await source.FetchHistoricalDataAsync(symbol, startDate, endDate);
        }

        public async Task<Dictionary<string, StockData>> GetStockListAsync(MarketType marketType, bool includeWarrant = false)
        {
            var source = GetSource(marketType);
            var list = await source.FetchStockListAsync(includeWarrant);
            
            foreach (var kvp in list) _stockCache[kvp.Key] = kvp.Value;
            
            return list;
        }

        public async Task<Dictionary<string, StockData>> GetAllStockListAsync(bool includeWarrant = false)
        {
            var result = new Dictionary<string, StockData>();
            
            foreach (var source in _sources)
            {
                var list = await source.FetchStockListAsync(includeWarrant);
                foreach (var item in list)
                {
                    result[item.Key] = item.Value;
                    _stockCache[item.Key] = item.Value;
                }
            }
            
            return result;
        }

        public void SubscribeMonitor(string symbol, IStockPriceObserver observer) => _priceSubject.Subscribe(symbol, observer);
        public void UnsubscribeMonitor(string symbol, IStockPriceObserver observer) => _priceSubject.Unsubscribe(symbol, observer);
    }
}
