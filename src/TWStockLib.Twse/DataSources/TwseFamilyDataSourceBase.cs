using Microsoft.Extensions.Logging;
using TWStockLib.Abstractions;
using TWStockLib.Cache;
using TWStockLib.Models;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources;

/// <summary>
/// 證交所家族（TSE / OTC）資料來源的共用基底。報價與股票清單的取得流程相同，
/// 只差市場代碼前綴 / ISIN strMode / 歷史端點，由子類別以抽象成員提供（Template Method）。
/// </summary>
public abstract class TwseFamilyDataSourceBase : IStockDataSource
{
    private static readonly TimeSpan QuoteTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DailyTtl = TimeSpan.FromDays(1);

    protected readonly IStockHttpFetcher Fetcher;
    protected readonly IStockParser Parser;
    protected readonly ICacheService Cache;
    protected readonly ILogger Logger;

    protected TwseFamilyDataSourceBase(
        IStockHttpFetcher fetcher, IStockParser parser, ICacheService cache, ILogger logger)
    {
        Fetcher = fetcher;
        Parser = parser;
        Cache = cache;
        Logger = logger;
    }

    public abstract MarketType Market { get; }

    /// <summary>即時報價 ex_ch 前綴："tse" 或 "otc"。</summary>
    protected abstract string MarketKeyPrefix { get; }

    /// <summary>ISIN 股票清單頁的 strMode：上市 2、上櫃 4。</summary>
    protected abstract int IsinStrMode { get; }

    public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, CancellationToken ct = default)
    {
        var cacheKey = $"realtime_quote_{Market}_{symbol}";
        return Cache.GetOrSetAsync(cacheKey, async () =>
        {
            var exCh = $"{MarketKeyPrefix}_{symbol}.tw";
            var url = $"http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={exCh}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
            var json = await Fetcher.GetStringAsync(url, ct);
            return Parser.ParseRealtimeQuote(json, Market);
        }, QuoteTtl);
    }

    public async Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var result = new List<StockHistory>();
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var lastMonth = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= lastMonth)
        {
            ct.ThrowIfCancellationRequested();
            var monthData = await FetchMonthAsync(symbol, current, ct);
            result.AddRange(monthData);
            current = current.AddMonths(1);
        }

        return result.Where(h => h.Date >= startDate && h.Date <= endDate).ToList();
    }

    public Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default)
    {
        var cacheKey = $"stock_list_{Market}_{includeWarrant}";
        return Cache.GetOrSetAsync(cacheKey, async () =>
        {
            var url = $"https://isin.twse.com.tw/isin/C_public.jsp?strMode={IsinStrMode}";
            var html = await Fetcher.GetBig5StringAsync(url, ct);
            return Parser.ParseStockList(html, Market, includeWarrant);
        }, DailyTtl);
    }

    /// <summary>取得單一月份的歷史資料（含快取），由子類別決定端點與解析方式。</summary>
    protected abstract Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateTime month, CancellationToken ct);

    /// <summary>共用的單月快取包裝，子類別在 fetch 委派內組 URL 與解析。</summary>
    protected Task<IReadOnlyList<StockHistory>> CachedMonth(
        string symbol, DateTime month, Func<Task<IReadOnlyList<StockHistory>>> fetch)
    {
        var cacheKey = $"history_{Market}_{symbol}_{month:yyyyMM}";
        return Cache.GetOrSetAsync(cacheKey, fetch, DailyTtl);
    }
}
