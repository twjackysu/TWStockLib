using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;
using TWStockLib.Cache;
using TWStockLib.Models;

namespace TWStockLib.Sources
{
    public class TwseSource : BaseStockSource
    {
        public override MarketType Market => MarketType.TSE;

        public TwseSource(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<TwseSource> logger) 
            : base(httpClientFactory, cacheService, logger)
        {
        }

        public override async Task<StockQuote> FetchRealtimeQuoteAsync(string symbol)
        {
            var cacheKey = $"realtime_quote_tse_{symbol}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var marketKey = $"tse_{symbol}.tw";
                    var url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={marketKey}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
                    
                    var json = await httpClient.GetStringAsync(url);
                    var jObj = JObject.Parse(json);
                    var jArray = jObj["msgArray"] as JArray;

                    if (jArray != null && jArray.Count > 0)
                    {
                        var item = jArray[0] as JObject;
                        return new StockQuote
                        {
                            Symbol = GetValue(item, "c"),
                            Market = Market,
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
                    _logger.LogError(ex, $"Error fetching TSE realtime quote for {symbol}");
                    return null;
                }
            }, TimeSpan.FromSeconds(5));
        }

        public override async Task<IEnumerable<StockHistory>> FetchHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            var result = new List<StockHistory>();
            var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

            while (currentDate <= endMonth)
            {
                var monthData = await FetchMonthData(symbol, currentDate);
                if (monthData != null) result.AddRange(monthData);
                currentDate = currentDate.AddMonths(1);
            }

            return result.Where(h => h.Date >= startDate && h.Date <= endDate);
        }

        private async Task<IEnumerable<StockHistory>> FetchMonthData(string symbol, DateTime month)
        {
            var cacheKey = $"history_tse_{symbol}_{month:yyyyMM}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={month:yyyyMM01}&stockNo={symbol}";
                    var json = await httpClient.GetStringAsync(url);
                    var jObj = JObject.Parse(json);

                    if (jObj["stat"]?.ToString() != "OK") return new List<StockHistory>();

                    var data = jObj["data"] as JArray;
                    return data?.Select(d => new StockHistory(d.ToObject<List<string>>())).ToList() ?? new List<StockHistory>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching TSE history for {symbol} {month:yyyyMM}");
                    return new List<StockHistory>();
                }
            }, TimeSpan.FromDays(1));
        }

        public override async Task<Dictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false)
        {
            var cacheKey = $"stock_list_tse_{includeWarrant}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var url = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=2";
                    
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var raw = await response.Content.ReadAsByteArrayAsync();
                    var html = Encoding.GetEncoding(950).GetString(raw);

                    var parser = new HtmlParser();
                    var doc = parser.ParseDocument(html);
                    var elements = doc.QuerySelectorAll("body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");

                    return elements
                        .Where(x => includeWarrant || (x.TextContent.Length > 3 && x.TextContent[^3] != '購' && x.TextContent[^3] != '售'))
                        .Select(x => x.TextContent.Split('　'))
                        .Where(x => x.Length >= 2)
                        .ToDictionary(x => x[0].Trim(), x => new StockData { Symbol = x[0].Trim(), Name = x[1].Trim(), Market = Market });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching TSE stock list");
                    return new Dictionary<string, StockData>();
                }
            }, TimeSpan.FromDays(1));
        }
    }
}
