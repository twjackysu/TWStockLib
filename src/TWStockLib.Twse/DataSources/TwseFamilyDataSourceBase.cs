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
    /// <summary>
    /// 一個 MIS 請求最多合併的代號數。實測（2026-10）一次 150 檔可以，200 檔（URL 約 2.5 KB）回 rtcode 9999
    /// 「參數不足」；取 50 留足餘裕，6 碼代號（ETF、權證）的 URL 也只有約 0.8 KB。
    /// </summary>
    internal const int MaxSymbolsPerQuoteRequest = 50;

    private static readonly TimeSpan TaiwanUtcOffset = TimeSpan.FromHours(8);

    protected readonly IStockHttpFetcher Fetcher;
    protected readonly ICacheService Cache;
    private readonly TimeProvider _timeProvider;
    private readonly TwStockOptions _options;

    // 「不含權證」的檢視依含權證清單的實例記住：同一份快取只過濾一次，快取換新後舊檢視跟著被回收
    private readonly ConditionalWeakTable<IReadOnlyDictionary<string, StockData>, IReadOnlyDictionary<string, StockData>>
        _withoutWarrants = new();

    // OpenAPI 上次合併的含權證清單；兩份快取都還是同一個實例時直接沿用，多執行緒同時換新頂多重算一次
    private MergedStockList? _withWarrants;

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

    /// <summary>
    /// 依代號判斷是否為權證（含牛熊證）。權證代號有固定的編碼區段，2026/10/22 以兩個市場 ISIN 頁面的 CFICode
    /// （「RW」開頭即權證）核對 53,863 檔，結果完全一致；以代號判斷時，沒有 CFICode 的來源也能用同一套規則。
    /// </summary>
    protected internal abstract bool IsWarrantSymbol(string symbol);

    public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default)
    {
        async Task<StockQuote?> Fetch(CancellationToken token)
            => (await FetchQuotesFromMisAsync([symbol], token)).FirstOrDefault(q => q.Symbol == symbol);

        return options.BypassCache
            ? Cache.RefreshAsync(QuoteCacheKey(symbol), () => Fetch(ct), _options.QuoteCacheDuration)
            : Cache.GetOrSetAsync(QuoteCacheKey(symbol), Fetch, _options.QuoteCacheDuration, ct);
    }

    /// <summary>
    /// 已快取的代號直接取用（<see cref="QuoteOptions.BypassCache"/> 時不取用），其餘每
    /// <see cref="MaxSymbolsPerQuoteRequest"/> 檔合併成一個 MIS 請求，查到的報價逐檔寫回快取，
    /// 之後的單檔查詢也拿得到。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StockQuote>> FetchRealtimeQuotesAsync(
        IReadOnlyList<string> symbols, QuoteOptions options, CancellationToken ct = default)
    {
        var quotes = new Dictionary<string, StockQuote>(symbols.Count);
        var toFetch = new List<string>(symbols.Count);
        foreach (var symbol in symbols)
        {
            // 工廠回 null 不會被快取（ICacheService 的約定），這裡只是查快取、不會寫入任何東西；
            // 用不帶 CancellationToken 的多載，不會加入或發起 single-flight 的載入
            var cached = options.BypassCache
                ? null
                : await Cache.GetOrSetAsync(QuoteCacheKey(symbol), () => Task.FromResult<StockQuote?>(null), _options.QuoteCacheDuration);
            if (cached is not null)
                quotes[symbol] = cached;
            else
                toFetch.Add(symbol);
        }

        foreach (var chunk in toFetch.Chunk(MaxSymbolsPerQuoteRequest))
        {
            var requested = chunk.ToHashSet();
            foreach (var quote in await FetchQuotesFromMisAsync(chunk, ct))
            {
                if (!requested.Contains(quote.Symbol))
                    continue;

                await Cache.RefreshAsync(
                    QuoteCacheKey(quote.Symbol), () => Task.FromResult<StockQuote?>(quote), _options.QuoteCacheDuration);
                quotes[quote.Symbol] = quote;
            }
        }

        return quotes.ToFrozenDictionary();
    }

    private string QuoteCacheKey(string symbol) => $"realtime_quote_{Market}_{symbol}";

    /// <summary>
    /// 以一個 MIS 請求查詢多檔：<c>ex_ch</c> 以「|」串接，上游依序每檔回一筆，查無資料的那筆略過。
    /// 回應中的代號與查詢時大小寫完全相同（MIS 的代號區分大小寫：00631L 查得到、00631l 查不到）。
    /// </summary>
    private async Task<IReadOnlyList<StockQuote>> FetchQuotesFromMisAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        // 跳脫讓「&」「#」這類字元改不了其他查詢參數；MIS 會先解碼再以「|」分隔多檔，
        // 擋下「2330.tw|tse_2317」的是 StockMarketService 的代號格式檢查
        var exCh = string.Join('|', symbols.Select(symbol => $"{MarketKeyPrefix}_{Uri.EscapeDataString(symbol)}.tw"));
        var url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={exCh}&json=1&delay=0&_={DateTime.UtcNow.Ticks}";
        var json = await Fetcher.GetStringAsync(url, ct);
        return TwseStockParser.ParseRealtimeQuotes(json, Market);
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
        => FetchStockListAsync(new StockListOptions { IncludeWarrant = includeWarrant }, ct);

    /// <summary>
    /// 每個上游的清單各自快取（鍵 <c>stock_list_{上游}_{Market}</c>），同時發生的未命中只下載一次。
    /// 含權證的清單只下載一次，不含權證的版本在讀取時依代號過濾。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        StockListOptions options, CancellationToken ct = default)
    {
        switch (options.Source)
        {
            case StockListSource.OpenApi:
                return await FetchOpenApiStockListAsync(options.IncludeWarrant, ct);
            case StockListSource.Isin:
                // ISIN 頁面有數 MB，含權證，只下載、解析一次
                var all = await CachedStockList($"stock_list_isin_{Market}", async token =>
                {
                    var url = $"https://isin.twse.com.tw/isin/C_public.jsp?strMode={IsinStrMode}";
                    var html = await Fetcher.GetBig5StringAsync(url, token);
                    return TwseStockParser.ParseStockList(html, Market);
                }, ct);
                return options.IncludeWarrant ? all : WithoutWarrants(all);
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Source, $"不支援的清單來源：{options.Source}");
        }
    }

    /// <summary>OpenAPI 上此市場權證以外的證券清單（不含權證）。</summary>
    protected abstract string OpenApiStockListUrl { get; }

    /// <summary>OpenAPI 上此市場的權證清單。</summary>
    protected abstract string OpenApiWarrantListUrl { get; }

    protected abstract IReadOnlyDictionary<string, StockData> ParseOpenApiStockList(string json);

    protected abstract IReadOnlyDictionary<string, StockData> ParseOpenApiWarrantList(string json);

    /// <summary>
    /// OpenAPI 的權證與其他證券分屬不同端點，權證清單大得多（上市解壓後約 45 MB、上櫃約 3.8 MB），只在需要時下載。
    /// 兩份分開快取、各自只下載一次，合併結果依兩份快取的實例記住。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, StockData>> FetchOpenApiStockListAsync(
        bool includeWarrant, CancellationToken ct)
    {
        var listed = await CachedStockList($"stock_list_openapi_{Market}",
            async token => ParseOpenApiStockList(await Fetcher.GetStringAsync(OpenApiStockListUrl, token)), ct);
        if (!includeWarrant)
            return listed;

        var warrants = await CachedStockList($"stock_list_openapi_{Market}_warrants",
            async token => ParseOpenApiWarrantList(await Fetcher.GetStringAsync(OpenApiWarrantListUrl, token)), ct);

        var merged = _withWarrants;
        if (merged is null || !ReferenceEquals(merged.Listed, listed) || !ReferenceEquals(merged.Warrants, warrants))
            _withWarrants = merged = new MergedStockList(listed, warrants);
        return merged.All;
    }

    /// <summary>OpenAPI 的兩份清單合併後的結果，連同合併時用的兩份快取實例。</summary>
    private sealed class MergedStockList(
        IReadOnlyDictionary<string, StockData> listed, IReadOnlyDictionary<string, StockData> warrants)
    {
        public IReadOnlyDictionary<string, StockData> Listed { get; } = listed;
        public IReadOnlyDictionary<string, StockData> Warrants { get; } = warrants;
        public IReadOnlyDictionary<string, StockData> All { get; } = listed.Concat(warrants)
            .DistinctBy(p => p.Key)
            .ToFrozenDictionary();
    }

    /// <summary>以股票清單的快取時間快取；<paramref name="fetch"/> 收到的是合併並行查詢後共用的 token。</summary>
    private Task<IReadOnlyDictionary<string, StockData>> CachedStockList(
        string cacheKey, Func<CancellationToken, Task<IReadOnlyDictionary<string, StockData>>> fetch, CancellationToken ct)
        => Cache.GetOrSetAsync(cacheKey, fetch, _options.StockListCacheDuration, ct);

    /// <summary>過濾掉權證；同一份清單只過濾一次。</summary>
    private IReadOnlyDictionary<string, StockData> WithoutWarrants(IReadOnlyDictionary<string, StockData> list)
        => _withoutWarrants.GetValue(list, all => all
            .Where(p => !IsWarrantSymbol(p.Key))
            .ToFrozenDictionary());

    /// <summary>台灣時間的今天。</summary>
    protected DateOnly TaiwanToday => DateOnly.FromDateTime(TaiwanNow.DateTime);

    private DateTimeOffset TaiwanNow => _timeProvider.GetUtcNow().ToOffset(TaiwanUtcOffset);

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
        var taiwanNow = TaiwanNow;
        var currentMonth = new DateOnly(taiwanNow.Year, taiwanNow.Month, 1);
        return new DateOnly(month.Year, month.Month, 1) < currentMonth
            ? _options.ClosedMonthHistoryCacheDuration
            : _options.CurrentMonthHistoryCacheDuration;
    }
}
