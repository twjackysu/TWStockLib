using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TWStockLib.Cache;
using TWStockLib.Models;
using TWStockLib.Twse.Internal;

namespace TWStockLib.Strategy
{
    public class TwseDataFetchStrategy : IDataFetchStrategy
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ICacheService _cacheService;
        private readonly ILogger<TwseDataFetchStrategy> _logger;

        static TwseDataFetchStrategy()
        {
            // 註冊編碼提供者，以支援 950 (繁體中文 Big5) 編碼
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public TwseDataFetchStrategy(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<TwseDataFetchStrategy> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<StockQuote> FetchRealtimeQuote(string symbol, MarketType marketType)
        {
            var cacheKey = $"realtime_quote_{marketType}_{symbol}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var marketKey = GetMarketKey(marketType, symbol);
                    var url = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={marketKey}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
                    var json = await httpClient.GetStringAsync(url);

                    var jObj = JObject.Parse(json);
                    var jArray = jObj["msgArray"] as JArray;

                    if (jArray != null && jArray.Count > 0)
                    {
                        var item = jArray[0] as JObject;
                        return new StockQuote
                        {
                            Symbol = GetValue(item, "c"),
                            Market = marketType,
                            Name = GetValue(item, "n"),
                            LastPrice = GetNullableDecimal(item, "z"),
                            LastVolume = GetUInt32(item, "tv"),
                            TotalVolume = GetUInt32(item, "v"),
                            Top5SellPrice = GetDecimalArray(item, "a"),
                            Top5SellVolume = GetUInt32Array(item, "f"),
                            Top5BuyPrice = GetDecimalArray(item, "b"),
                            Top5BuyVolume = GetUInt32Array(item, "g"),
                            SyncTime = GetDateTime(item, "tlong"),
                            HighestPrice = GetNullableDecimal(item, "h"),
                            LowestPrice = GetNullableDecimal(item, "l"),
                            OpeningPrice = GetNullableDecimal(item, "o"),
                            YesterdayClosingPrice = GetDecimal(item, "y"),
                            LimitUp = GetDecimal(item, "u"),
                            LimitDown = GetDecimal(item, "w")
                        };
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching realtime quote for {symbol}");
                    throw;
                }
            }, TimeSpan.FromSeconds(30));
        }

        public async Task<IEnumerable<StockHistory>> FetchHistoricalData(string symbol, DateTime startDate, DateTime endDate, MarketType marketType)
        {
            var result = new List<StockHistory>();
            var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

            while (currentDate <= endMonth)
            {
                var monthData = await FetchMonthHistoricalData(symbol, currentDate, marketType);
                if (monthData != null)
                {
                    result.AddRange(monthData);
                }
                currentDate = currentDate.AddMonths(1);
            }

            return result.Where(h => h.Date >= startDate && h.Date <= endDate);
        }

        private async Task<IEnumerable<StockHistory>> FetchMonthHistoricalData(string symbol, DateTime month, MarketType marketType)
        {
            var cacheKey = $"history_{marketType}_{symbol}_{month:yyyyMM}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();

                    if (marketType == MarketType.TSE)
                    {
                        var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={month:yyyyMM01}&stockNo={symbol}";
                        var json = await httpClient.GetStringAsync(url);
                        var jObj = JObject.Parse(json);

                        if (jObj["stat"].ToString() != "OK")
                        {
                            return new List<StockHistory>();
                        }

                        var data = jObj["data"] as JArray;
                        if (data == null)
                        {
                            return new List<StockHistory>();
                        }

                        return data.Select(d => new StockHistory(d.ToObject<List<string>>())).ToList();
                    }
                    else // OTC
                    {
                        var url = $"http://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock/st43_result.php?l=zh-tw&date={month:yyyy/MM/dd}&code={symbol}";
                        var response = await httpClient.GetAsync(url);

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning($"URL failed with status {response.StatusCode}: {url}");
                            return new List<StockHistory>();
                        }

                        var content = await response.Content.ReadAsStringAsync();


                        var tpex = JsonConvert.DeserializeObject<TPEXAPIModel>(content);
                        if (tpex?.tables != null && tpex.tables.Count > 0)
                        {
                            var firstTable = tpex.tables.FirstOrDefault();
                            if (firstTable?.data != null && firstTable.data.Count > 0)
                            {
                                _logger.LogInformation($"Successfully fetched data for {symbol} using URL: {url}");
                                return firstTable.data.Select(d => new StockHistory(d)).ToList();
                            }
                        }

                        _logger.LogError($"All attempts to fetch data for {symbol} failed");
                        return new List<StockHistory>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching historical data for {symbol} for {month:yyyyMM}");
                    return new List<StockHistory>();
                }
            }, TimeSpan.FromDays(1));
        }

        public async Task<Dictionary<string, StockData>> FetchStockList(MarketType marketType, bool includeWarrant = false)
        {
            var cacheKey = $"stock_list_{marketType}_{includeWarrant}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    string url;

                    if (marketType == MarketType.TSE)
                    {
                        url = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=2";
                    }
                    else
                    {
                        url = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=4";
                    }

                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var raw = await response.Content.ReadAsByteArrayAsync();
                    var htmlCode = Encoding.GetEncoding(950).GetString(raw);

                    var parser = new HtmlParser();
                    var doc = parser.ParseDocument(htmlCode);
                    IEnumerable<IElement> elements;

                    if (marketType == MarketType.TSE)
                    {
                        elements = doc.QuerySelectorAll("body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");
                    }
                    else
                    {
                        elements = doc.QuerySelectorAll("body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");
                    }

                    return elements
                        .Where(x => includeWarrant || (x.TextContent[x.TextContent.Length - 3] != '購' && x.TextContent[x.TextContent.Length - 3] != '售'))
                        .Select(x => x.TextContent.Split('　'))
                        .ToDictionary(
                            x => x[0],
                            x => new StockData { Symbol = x[0], Name = x[1], Market = marketType }
                        );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching stock list for {marketType}");
                    return new Dictionary<string, StockData>();
                }
            }, TimeSpan.FromDays(1));
        }

        #region Helper Methods
        private string GetMarketKey(MarketType marketType, string symbol)
        {
            return marketType == MarketType.TSE
                ? $"tse_{symbol}.tw"
                : $"otc_{symbol}.tw";
        }

        private string GetValue(JObject jObject, string key)
        {
            return jObject[key]?.ToString() ?? string.Empty;
        }

        private decimal? GetNullableDecimal(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value) || value == "-")
                return null;
            return decimal.TryParse(value, out decimal result) ? result : (decimal?)null;
        }

        private decimal GetDecimal(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value) || value == "-")
                return 0;
            return decimal.TryParse(value, out decimal result) ? result : 0;
        }

        private uint? GetUInt32(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value) || value == "-")
                return null;
            return uint.TryParse(value.Replace(",", ""), out uint result) ? result : (uint?)null;
        }

        private DateTime? GetDateTime(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value) || value == "-")
                return null;
            return long.TryParse(value, out long timestamp) 
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime 
                : (DateTime?)null;
        }

        private decimal[] GetDecimalArray(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value))
                return new decimal[0];

            return value.Split('_')
                .Where(x => !string.IsNullOrEmpty(x) && x != "-")
                .Select(x => decimal.TryParse(x, out decimal result) ? result : 0)
                .ToArray();
        }

        private uint[] GetUInt32Array(JObject jObject, string key)
        {
            var value = jObject[key]?.ToString();
            if (string.IsNullOrEmpty(value))
                return new uint[0];

            return value.Split('_')
                .Where(x => !string.IsNullOrEmpty(x) && x != "-")
                .Select(x => uint.TryParse(x.Replace(",", ""), out uint result) ? result : 0)
                .ToArray();
        }
        #endregion
    }
}