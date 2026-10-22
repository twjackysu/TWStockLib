using System.Globalization;
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
internal abstract class TwseFamilyDataSourceBase : IStockDataSource
{
    private static readonly TimeSpan QuoteTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DailyTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan CurrentMonthHistoryTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TaiwanUtcOffset = TimeSpan.FromHours(8);

    protected readonly IStockHttpFetcher Fetcher;
    protected readonly ICacheService Cache;
    private readonly TimeProvider _timeProvider;

    protected TwseFamilyDataSourceBase(IStockHttpFetcher fetcher, ICacheService cache, TimeProvider timeProvider)
    {
        Fetcher = fetcher;
        Cache = cache;
        _timeProvider = timeProvider;
    }

    public abstract MarketType Market { get; }

    /// <summary>即時報價 ex_ch 前綴："tse" 或 "otc"。</summary>
    protected abstract string MarketKeyPrefix { get; }

    /// <summary>ISIN 股票清單頁的 strMode：上市 2、上櫃 4。</summary>
    protected abstract int IsinStrMode { get; }

    public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default)
    {
        var cacheKey = $"realtime_quote_{Market}_{symbol}";
        async Task<StockQuote?> Fetch()
        {
            var exCh = $"{MarketKeyPrefix}_{symbol}.tw";
            var url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={exCh}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
            var json = await Fetcher.GetStringAsync(url, ct);
            return TwseStockParser.ParseRealtimeQuote(json, Market);
        }

        return options.BypassCache
            ? Cache.RefreshAsync(cacheKey, Fetch, QuoteTtl)
            : Cache.GetOrSetAsync(cacheKey, Fetch, QuoteTtl);
    }

    public async Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = new List<StockHistory>();
        var month = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);

        while (month <= lastMonth)
        {
            ct.ThrowIfCancellationRequested();
            var monthData = await FetchMonthAsync(symbol, month, ct);
            result.AddRange(monthData.Where(h => h.Date >= from && h.Date <= to));
            month = month.AddMonths(1);
        }

        return result.AsReadOnly();
    }

    public Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default)
    {
        var cacheKey = $"stock_list_{Market}_{includeWarrant}";
        return Cache.GetOrSetAsync(cacheKey, async () =>
        {
            var url = $"https://isin.twse.com.tw/isin/C_public.jsp?strMode={IsinStrMode}";
            var html = await Fetcher.GetBig5StringAsync(url, ct);
            return TwseStockParser.ParseStockList(html, Market, includeWarrant);
        }, DailyTtl);
    }

    /// <summary>取得單一月份的歷史資料（含快取），由子類別決定端點與解析方式。</summary>
    protected abstract Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateOnly month, CancellationToken ct);

    /// <summary>共用的單月快取包裝，子類別在 fetch 委派內組 URL 與解析。</summary>
    protected Task<IReadOnlyList<StockHistory>> CachedMonth(
        string symbol, DateOnly month, Func<Task<IReadOnlyList<StockHistory>>> fetch)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"history_{Market}_{symbol}_{month:yyyyMM}");
        return Cache.GetOrSetAsync(cacheKey, fetch, HistoryTtl(month));
    }

    /// <summary>
    /// 已結束的月份資料不再變動，快取一天；當月（台灣時間）每個交易日收盤後會多一列，只短暫快取，
    /// 未來月份同理，避免月份開始後仍拿到先前快取的空資料。
    /// </summary>
    private TimeSpan HistoryTtl(DateOnly month)
    {
        var taiwanNow = _timeProvider.GetUtcNow().ToOffset(TaiwanUtcOffset);
        var currentMonth = new DateOnly(taiwanNow.Year, taiwanNow.Month, 1);
        return new DateOnly(month.Year, month.Month, 1) < currentMonth ? DailyTtl : CurrentMonthHistoryTtl;
    }
}
