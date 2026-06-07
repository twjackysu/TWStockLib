using Microsoft.Extensions.Logging;
using TWStockLib.Abstractions;
using TWStockLib.Models;
using TWStockLib.Observer;

namespace TWStockLib.Services
{
    /// <summary>
    /// 對外門面（Facade）：依 <see cref="MarketType"/> 路由到對應的 <see cref="IStockDataSource"/>，
    /// 統一例外處理為 <see cref="StockResult{T}"/>，並在報價更新時通知價格觀察者。
    /// </summary>
    public class StockMarketService : IStockMarketService
    {
        private readonly StockPriceSubject _priceSubject = new();
        private readonly IReadOnlyDictionary<MarketType, IStockDataSource> _sources;
        private readonly ILogger<StockMarketService> _logger;

        public StockMarketService(IEnumerable<IStockDataSource> sources, ILogger<StockMarketService> logger)
        {
            _sources = sources.ToDictionary(s => s.Market);
            _logger = logger;
        }

        public async Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
            string symbol, MarketType market, CancellationToken ct = default)
        {
            if (!_sources.TryGetValue(market, out var source))
                return StockResult<StockQuote>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

            try
            {
                var quote = await source.FetchRealtimeQuoteAsync(symbol, ct);
                if (quote is null)
                    return StockResult<StockQuote>.Fail(StockErrorCodes.NotFound, $"查無報價：{symbol}");

                if (quote.LastPrice.HasValue)
                    _priceSubject.UpdatePrice(symbol, quote.LastPrice.Value);

                return StockResult<StockQuote>.Ok(quote);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得即時報價失敗：{Symbol} ({Market})", symbol, market);
                return StockResult<StockQuote>.Fail(StockErrorCodes.UpstreamError, ex.Message);
            }
        }

        public async Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
            string symbol, DateTime startDate, DateTime endDate, MarketType market, CancellationToken ct = default)
        {
            if (!_sources.TryGetValue(market, out var source))
                return StockResult<IReadOnlyList<StockHistory>>.Fail(StockErrorCodes.SourceNotFound, $"不支援的市場：{market}");

            try
            {
                var history = await source.FetchHistoricalDataAsync(symbol, startDate, endDate, ct);
                return StockResult<IReadOnlyList<StockHistory>>.Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得歷史資料失敗：{Symbol} ({Market})", symbol, market);
                return StockResult<IReadOnlyList<StockHistory>>.Fail(StockErrorCodes.UpstreamError, ex.Message);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得股票清單失敗：{Market}", market);
                return StockResult<IReadOnlyDictionary<string, StockData>>.Fail(StockErrorCodes.UpstreamError, ex.Message);
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
                return StockResult<IReadOnlyDictionary<string, StockData>>.Ok(merged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得完整股票清單失敗");
                return StockResult<IReadOnlyDictionary<string, StockData>>.Fail(StockErrorCodes.UpstreamError, ex.Message);
            }
        }

        public void SubscribePriceChanges(string symbol, IStockPriceObserver observer)
            => _priceSubject.Subscribe(symbol, observer);

        public void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer)
            => _priceSubject.Unsubscribe(symbol, observer);
    }
}
