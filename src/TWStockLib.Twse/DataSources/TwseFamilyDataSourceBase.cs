using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using TWStockLib.Abstractions;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources;

/// <summary>
/// 證交所家族（TSE / OTC）資料來源的共用基底。報價與股票清單的取得流程相同，
/// 只差市場代碼前綴 / ISIN strMode / 歷史端點，由子類別以抽象成員提供（Template Method）。
/// </summary>
internal abstract class TwseFamilyDataSourceBase : IStockDataSource
{
    private static readonly TimeSpan TaiwanUtcOffset = TimeSpan.FromHours(8);

    protected readonly IStockHttpFetcher Fetcher;
    protected readonly ICacheService Cache;
    private readonly TimeProvider _timeProvider;
    private readonly TwStockOptions _options;

    // 「不含權證」的檢視依完整清單的實例記住：同一份快取只過濾一次，快取換新後舊檢視跟著被回收
    private readonly ConditionalWeakTable<IReadOnlyDictionary<string, StockData>, IReadOnlyDictionary<string, StockData>>
        _withoutWarrants = new();

    // options 未指定（直接 new 的測試）時使用 TwStockOptions 的預設快取時間
    protected TwseFamilyDataSourceBase(
        IStockHttpFetcher fetcher, ICacheService cache, TimeProvider timeProvider, IOptions<TwStockOptions>? options)
    {
        Fetcher = fetcher;
        Cache = cache;
        _timeProvider = timeProvider;
        _options = options?.Value ?? new TwStockOptions();
    }

    public abstract MarketType Market { get; }

    /// <summary>即時報價 ex_ch 前綴："tse" 或 "otc"。</summary>
    protected abstract string MarketKeyPrefix { get; }

    /// <summary>ISIN 股票清單頁的 strMode：上市 2、上櫃 4。</summary>
    protected abstract int IsinStrMode { get; }

    public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default)
    {
        var cacheKey = $"realtime_quote_{Market}_{symbol}";
        async Task<StockQuote?> Fetch(CancellationToken token)
        {
            var exCh = $"{MarketKeyPrefix}_{symbol}.tw";
            var url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={exCh}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
            var json = await Fetcher.GetStringAsync(url, token);
            return TwseStockParser.ParseRealtimeQuote(json, Market);
        }

        return options.BypassCache
            ? Cache.RefreshAsync(cacheKey, () => Fetch(ct), _options.QuoteCacheDuration)
            : Cache.GetOrSetAsync(cacheKey, Fetch, _options.QuoteCacheDuration, ct);
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

    /// <summary>
    /// ISIN 頁面有數 MB，每個市場只下載、解析一次並快取完整清單（含權證）；
    /// 是否排除權證在讀取時從同一份清單過濾，兩種查詢共用一次下載，同時發生的未命中也只下載一次。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default)
    {
        var cacheKey = $"stock_list_{Market}";
        var all = await Cache.GetOrSetAsync(cacheKey, async token =>
        {
            var url = $"https://isin.twse.com.tw/isin/C_public.jsp?strMode={IsinStrMode}";
            var html = await Fetcher.GetBig5StringAsync(url, token);
            return TwseStockParser.ParseStockList(html, Market);
        }, _options.StockListCacheDuration, ct);

        return includeWarrant
            ? all
            : _withoutWarrants.GetValue(all, static list => list
                .Where(p => p.Value.SecurityType != SecurityType.Warrant)
                .ToFrozenDictionary());
    }

    /// <summary>取得單一月份的歷史資料（含快取），由子類別決定端點與解析方式。</summary>
    protected abstract Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateOnly month, CancellationToken ct);

    /// <summary>
    /// 共用的單月快取包裝，子類別在 fetch 委派內組 URL 與解析；委派收到的 token 是快取合併並行查詢後共用的，
    /// 向上游查詢時要用它而不是呼叫端的 <paramref name="ct"/>。
    /// </summary>
    protected Task<IReadOnlyList<StockHistory>> CachedMonth(
        string symbol, DateOnly month, Func<CancellationToken, Task<IReadOnlyList<StockHistory>>> fetch, CancellationToken ct)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"history_{Market}_{symbol}_{month:yyyyMM}");
        return Cache.GetOrSetAsync(cacheKey, fetch, HistoryTtl(month), ct);
    }

    /// <summary>
    /// 已結束的月份資料不再變動，快取較久（預設一天）；當月（台灣時間）每個交易日收盤後會多一列，只短暫快取，
    /// 未來月份同理，避免月份開始後仍拿到先前快取的空資料。
    /// </summary>
    private TimeSpan HistoryTtl(DateOnly month)
    {
        var taiwanNow = _timeProvider.GetUtcNow().ToOffset(TaiwanUtcOffset);
        var currentMonth = new DateOnly(taiwanNow.Year, taiwanNow.Month, 1);
        return new DateOnly(month.Year, month.Month, 1) < currentMonth
            ? _options.ClosedMonthHistoryCacheDuration
            : _options.CurrentMonthHistoryCacheDuration;
    }
}
