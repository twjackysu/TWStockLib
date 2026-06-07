using Microsoft.Extensions.Logging;
using TWStockLib.Cache;
using TWStockLib.Strategy;

namespace TWStockLib.Factory
{
    public class TwseMarketFactory : IStockMarketFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ICacheService _cacheService;
        private readonly ILoggerFactory _loggerFactory;

        public TwseMarketFactory(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            _cacheService = cacheService;
            _loggerFactory = loggerFactory;
        }

        public IDataFetchStrategy CreateDataFetchStrategy()
        {
            return new TwseDataFetchStrategy(
                _httpClientFactory, 
                _cacheService, 
                _loggerFactory.CreateLogger<TwseDataFetchStrategy>());
        }
    }

} 