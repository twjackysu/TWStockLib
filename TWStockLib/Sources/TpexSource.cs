using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TWStockLib.Cache;
using TWStockLib.Models;

namespace TWStockLib.Sources
{
    public class TpexSource : BaseStockSource
    {
        public override MarketType Market => MarketType.OTC;

        public TpexSource(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<TpexSource> logger) 
            : base(httpClientFactory, cacheService, logger)
        {
        }

        public override async Task<StockQuote> FetchRealtimeQuoteAsync(string symbol)
        {
            var cacheKey = $"realtime_quote_otc_{symbol}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var marketKey = $"otc_{symbol}.tw";
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
                    _logger.LogError(ex, $"Error fetching OTC realtime quote for {symbol}");
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
            var cacheKey = $"history_otc_{symbol}_{month:yyyyMM}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var url = $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock/st43_result.php?l=zh-tw&date={month:yyyy/MM/dd}&code={symbol}";
                    var response = await httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"TPEX URL failed: {url}");
                        return new List<StockHistory>();
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var tpexModel = JsonConvert.DeserializeObject<TPEXAPIModel>(content);
                    
                    if (tpexModel?.tables != null && tpexModel.tables.Count > 0)
                    {
                        var data = tpexModel.tables[0].data;
                        if (data != null)
                        {
                            return data.Select(d => new StockHistory(d)).ToList();
                        }
                    }
                    return new List<StockHistory>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching OTC history for {symbol} {month:yyyyMM}");
                    return new List<StockHistory>();
                }
            }, TimeSpan.FromDays(1));
        }

        public override async Task<Dictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false)
        {
            var cacheKey = $"stock_list_otc_{includeWarrant}";
            return await _cacheService.GetOrSetAsync(cacheKey, async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var url = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=4"; // OTC Mode
                    
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
                    _logger.LogError(ex, "Error fetching OTC stock list");
                    return new Dictionary<string, StockData>();
                }
            }, TimeSpan.FromDays(1));
        }
    }
    
    // Internal model for TPEX JSON deserialization
    class TPEXAPIModel
    {
        public List<Table> tables { get; set; }
        public class Table
        {
            public List<List<string>> data { get; set; }
        }
    }
}
