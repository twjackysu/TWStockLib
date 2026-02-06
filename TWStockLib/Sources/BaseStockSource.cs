using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;
using TWStockLib.Cache;
using TWStockLib.Models;

namespace TWStockLib.Sources
{
    public abstract class BaseStockSource : IStockSource
    {
        protected readonly IHttpClientFactory _httpClientFactory;
        protected readonly ICacheService _cacheService;
        protected readonly ILogger _logger;

        public abstract MarketType Market { get; }

        protected BaseStockSource(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger logger)
        {
            _httpClientFactory = httpClientFactory;
            _cacheService = cacheService;
            _logger = logger;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public abstract Task<StockQuote> FetchRealtimeQuoteAsync(string symbol);
        public abstract Task<IEnumerable<StockHistory>> FetchHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate);
        public abstract Task<Dictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false);

        #region Helper Methods
        protected string GetValue(JObject jObject, string key) => jObject[key]?.ToString() ?? string.Empty;

        protected decimal? GetNullableDecimal(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            return (string.IsNullOrEmpty(value) || value == "-") ? null : (decimal.TryParse(value, out var res) ? res : (decimal?)null);
        }

        protected decimal GetDecimal(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            return (string.IsNullOrEmpty(value) || value == "-") ? 0 : (decimal.TryParse(value, out var res) ? res : 0);
        }

        protected uint? GetUInt32(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            return (string.IsNullOrEmpty(value) || value == "-") ? null : (uint.TryParse(value.Replace(",", ""), out var res) ? res : (uint?)null);
        }

        protected DateTime? GetDateTime(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            return (string.IsNullOrEmpty(value) || value == "-") ? null : (long.TryParse(value, out var timestamp) ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime : (DateTime?)null);
        }

        protected decimal[] GetDecimalArray(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value)) return Array.Empty<decimal>();
            return value.Split('_').Where(x => !string.IsNullOrEmpty(x) && x != "-").Select(x => decimal.TryParse(x, out var r) ? r : 0).ToArray();
        }

        protected uint[] GetUInt32Array(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value)) return Array.Empty<uint>();
            return value.Split('_').Where(x => !string.IsNullOrEmpty(x) && x != "-").Select(x => uint.TryParse(x.Replace(",", ""), out var r) ? r : 0).ToArray();
        }
        #endregion
    }
}
