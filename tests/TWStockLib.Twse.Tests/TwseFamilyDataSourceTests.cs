using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Abstractions;
using TWStockLib.Twse.DataSources;
using TWStockLib.Twse.Http;

namespace TWStockLib.Twse.Tests;

public class TwseFamilyDataSourceTests
{
    private static string Json(string price) =>
        "{\"msgArray\":[{\"c\":\"2330\",\"n\":\"台積電\",\"z\":\"" + price + "\",\"y\":\"596.00\",\"tlong\":\"1699000000000\"}]}";

    private sealed class FakeFetcher(params string[] responses) : IStockHttpFetcher
    {
        private int _next;
        public List<string> Urls { get; } = [];

        public Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            Urls.Add(url);
            return Task.FromResult(responses[Math.Min(_next++, responses.Length - 1)]);
        }

        public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static TwseStockDataSource Build(IStockHttpFetcher fetcher) =>
        new(fetcher, new MemoryCacheService(new MemoryCache(new MemoryCacheOptions())), TimeProvider.System);

    [Fact]
    public async Task FetchRealtimeQuoteAsync_RepeatedCalls_HitTheUpstreamOnce()
    {
        var fetcher = new FakeFetcher(Json("600.00"), Json("610.00"));
        var source = Build(fetcher);

        var first = await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);
        var second = await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        Assert.Equal(600.00m, first!.LastPrice);
        Assert.Same(first, second);
        Assert.Single(fetcher.Urls);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_NoCache_HitsTheUpstreamAgain()
    {
        var fetcher = new FakeFetcher(Json("600.00"), Json("610.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        var fresh = await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.NoCache);

        Assert.Equal(610.00m, fresh!.LastPrice);
        Assert.Equal(2, fetcher.Urls.Count);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_AfterANoCacheCall_NormalCallsSeeTheNewerQuote()
    {
        var fetcher = new FakeFetcher(Json("600.00"), Json("610.00"), Json("620.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.NoCache);

        var later = await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        Assert.Equal(610.00m, later!.LastPrice);
        Assert.Equal(2, fetcher.Urls.Count);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_NoCache_DoesNotTouchOtherSymbols()
    {
        var fetcher = new FakeFetcher(Json("600.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        await source.FetchRealtimeQuoteAsync("2317", QuoteOptions.NoCache);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        Assert.Equal(2, fetcher.Urls.Count);
        Assert.Contains("ex_ch=tse_2317.tw", fetcher.Urls[1]);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_AnIndexSymbol_UsesTheSameEndpoint()
    {
        // 加權指數在 MIS 的代號是 t00：不需要特殊 API，直接當作 TSE 的一檔「股票」查。
        var fetcher = new FakeFetcher(
            "{\"msgArray\":[{\"c\":\"t00\",\"n\":\"發行量加權股價指數\",\"z\":\"23100.12\",\"y\":\"22950.00\",\"u\":\"-\",\"w\":\"-\",\"tlong\":\"1791782705000\"}]}");
        var source = Build(fetcher);

        var quote = await source.FetchRealtimeQuoteAsync("t00", QuoteOptions.NoCache);

        Assert.Contains("ex_ch=tse_t00.tw", fetcher.Urls[0]);
        Assert.Equal(23100.12m, quote!.LastPrice);
        Assert.Equal(22950.00m, quote.YesterdayClosingPrice);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1791782705000), quote.SyncTime);
        Assert.Equal(TimeSpan.FromHours(8), quote.SyncTime!.Value.Offset);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_AnIndexBeforeItsFirstCalculation_HasNoLastPrice()
    {
        var fetcher = new FakeFetcher(
            "{\"msgArray\":[{\"c\":\"t00\",\"z\":\"-\",\"y\":\"22950.00\",\"u\":\"-\",\"w\":\"-\",\"tlong\":\"1791782705000\"}]}");

        var quote = await Build(fetcher).FetchRealtimeQuoteAsync("t00", QuoteOptions.NoCache);

        Assert.Null(quote!.LastPrice);
        Assert.Equal(22950.00m, quote.YesterdayClosingPrice);
    }

    // ── 股票清單 ──────────────────────────────────────────────────────────────

    /// <summary>依 URL 回應固定內容並記錄請求；沒有對應的 URL 時拋出例外。</summary>
    private sealed class ListFetcher(params (string Url, string Response)[] responses) : IStockHttpFetcher
    {
        public List<string> Urls { get; } = [];

        public Task<string> GetStringAsync(string url, CancellationToken ct = default) => Respond(url);

        public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default) => Respond(url);

        private Task<string> Respond(string url)
        {
            lock (Urls)
                Urls.Add(url);
            foreach (var (expected, response) in responses)
            {
                if (expected == url)
                    return Task.FromResult(response);
            }
            throw new HttpRequestException($"未預期的請求：{url}");
        }
    }

    private const string TseIsinUrl = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=2";
    private const string OtcIsinUrl = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=4";
    private const string TseStockDayAllUrl = "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL";
    private const string TseWarrantsUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap37_L";
    private const string TpexQuotesUrl = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";
    private const string TpexWarrantsUrl = "https://www.tpex.org.tw/openapi/v1/tpex_warrant_daily_quts";

    private static readonly StockListOptions Isin = new() { Source = StockListSource.Isin };
    private static readonly StockListOptions IsinWithWarrants = new() { Source = StockListSource.Isin, IncludeWarrant = true };

    /// <summary>fixture 的「今天」：2026/10/22（台灣時間），03080T 今天是最後交易日、03001T 已到期。</summary>
    private static readonly TimeProvider Oct22 =
        new FixedTimeProvider(DateTimeOffset.Parse("2026-10-22T10:00:00+08:00", CultureInfo.InvariantCulture));

    private static ICacheService NewCache() => new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));

    private static ListFetcher TseOpenApi() => new(
        (TseStockDayAllUrl, OpenApiFixtures.TwseStockDayAll), (TseWarrantsUrl, OpenApiFixtures.TwseWarrants));

    [Fact]
    public async Task FetchStockListAsync_ByDefault_UsesOpenApi_WithoutDownloadingTheWarrants()
    {
        var fetcher = TseOpenApi();
        var source = new TwseStockDataSource(fetcher, NewCache(), Oct22);

        var list = await source.FetchStockListAsync();

        Assert.Equal(["0050", "00631L", "01001T", "020000", "1101", "2330", "2887Z1", "910322"], list.Keys.Order());
        Assert.Equal(new StockData { Symbol = "2330", Name = "台積電", Market = MarketType.TSE }, list["2330"]);
        Assert.Equal([TseStockDayAllUrl], fetcher.Urls);
        Assert.True(((ICollection<KeyValuePair<string, StockData>>)list).IsReadOnly);
    }

    [Fact]
    public async Task FetchStockListAsync_OpenApiWithWarrants_AddsOnlyTheWarrantsStillTrading()
    {
        var fetcher = TseOpenApi();
        var source = new TwseStockDataSource(fetcher, NewCache(), Oct22);

        var all = await source.FetchStockListAsync(includeWarrant: true);

        Assert.Equal(12, all.Count);
        Assert.All(["030001", "03004T", "03726B", "03080T"], symbol => Assert.Contains(symbol, all.Keys));
        Assert.DoesNotContain("03001T", all.Keys); // 10/14 已到期
        Assert.Equal("台積電富邦64熊01", all["03726B"].Name);
        Assert.Equal([TseStockDayAllUrl, TseWarrantsUrl], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_OpenApiWithWarrants_IsMergedOncePerDownload()
    {
        var fetcher = TseOpenApi();
        var source = new TwseStockDataSource(fetcher, NewCache(), Oct22);

        var first = await source.FetchStockListAsync(includeWarrant: true);
        var withoutWarrants = await source.FetchStockListAsync();
        var second = await source.FetchStockListAsync(includeWarrant: true);

        Assert.Same(first, second);
        Assert.Equal(8, withoutWarrants.Count);
        Assert.Equal([TseStockDayAllUrl, TseWarrantsUrl], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_TheWarrantCutoff_IsTheTaiwanDate()
    {
        // UTC 還是 10/22，台灣已經是 10/23：今天最後交易的 03080T 不再列出
        var source = new TwseStockDataSource(TseOpenApi(), NewCache(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-10-22T16:30:00+00:00", CultureInfo.InvariantCulture)));

        var all = await source.FetchStockListAsync(includeWarrant: true);

        Assert.DoesNotContain("03080T", all.Keys);
        Assert.Contains("030001", all.Keys);
    }

    [Fact]
    public async Task FetchStockListAsync_OtcOpenApi_DownloadsTheWarrantsOnlyWhenAsked()
    {
        var fetcher = new ListFetcher((TpexQuotesUrl, OpenApiFixtures.TpexQuotes), (TpexWarrantsUrl, OpenApiFixtures.TpexWarrants));
        var source = new TpexStockDataSource(fetcher, NewCache(), Oct22);

        var withoutWarrants = await source.FetchStockListAsync();
        Assert.Equal([TpexQuotesUrl], fetcher.Urls);
        var all = await source.FetchStockListAsync(includeWarrant: true);

        Assert.Equal(["00679B", "020001", "6510", "8349A"], withoutWarrants.Keys.Order());
        Assert.Equal(["00679B", "020001", "6510", "700019", "72479U", "8349A"], all.Keys.Order());
        Assert.All(all.Values, s => Assert.Equal(MarketType.OTC, s.Market));
        Assert.Equal([TpexQuotesUrl, TpexWarrantsUrl], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_Isin_WithAndWithoutWarrants_ShareOneDownload()
    {
        var fetcher = new ListFetcher((TseIsinUrl, IsinFixtures.Tse));
        var source = new TwseStockDataSource(fetcher, NewCache(), Oct22);

        var withoutWarrants = await source.FetchStockListAsync(Isin);
        var all = await source.FetchStockListAsync(IsinWithWarrants);

        Assert.Equal(11, withoutWarrants.Count);
        Assert.Equal(15, all.Count);
        Assert.Equal([TseIsinUrl], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_IsinWithoutWarrants_ExcludesBullBearCertificates_AndIsFilteredOnce()
    {
        var source = new TwseStockDataSource(new ListFetcher((TseIsinUrl, IsinFixtures.Tse)), NewCache(), Oct22);

        var list = await source.FetchStockListAsync(Isin);

        Assert.DoesNotContain("030001", list.Keys); // 認購
        Assert.DoesNotContain("03004T", list.Keys); // 認售
        Assert.DoesNotContain("03989C", list.Keys); // 牛證
        Assert.DoesNotContain("03726B", list.Keys); // 熊證
        Assert.Contains("0050", list.Keys);
        Assert.Same(list, await source.FetchStockListAsync(Isin));
    }

    [Fact]
    public async Task FetchStockListAsync_IsinOtc_UsesItsOwnPage()
    {
        var fetcher = new ListFetcher((OtcIsinUrl, IsinFixtures.Otc));

        var otc = await new TpexStockDataSource(fetcher, NewCache(), Oct22).FetchStockListAsync(IsinWithWarrants);

        Assert.Equal([OtcIsinUrl], fetcher.Urls);
        Assert.Equal(7, otc.Count);
        Assert.All(otc.Values, s => Assert.Equal(MarketType.OTC, s.Market));
    }

    [Fact]
    public async Task FetchStockListAsync_EachUpstream_HasItsOwnCacheEntry()
    {
        var fetcher = new ListFetcher((TseIsinUrl, IsinFixtures.Tse), (TseStockDayAllUrl, OpenApiFixtures.TwseStockDayAll));
        var source = new TwseStockDataSource(fetcher, NewCache(), Oct22);

        var fromIsin = await source.FetchStockListAsync(Isin);
        var fromOpenApi = await source.FetchStockListAsync();

        Assert.Equal(11, fromIsin.Count);
        Assert.Equal(8, fromOpenApi.Count);
        Assert.Equal([TseIsinUrl, TseStockDayAllUrl], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_AnUndefinedSource_Throws()
    {
        var source = new TwseStockDataSource(TseOpenApi(), NewCache(), Oct22);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => source.FetchStockListAsync(new StockListOptions { Source = (StockListSource)42 }));
    }

    [Theory]
    [InlineData("030001", true)]   // 認購
    [InlineData("03004T", true)]   // 認售
    [InlineData("03989C", true)]   // 牛證
    [InlineData("03726B", true)]   // 熊證
    [InlineData("085555", true)]
    [InlineData("2330", false)]
    [InlineData("0050", false)]
    [InlineData("00631L", false)]  // ETF：00 開頭
    [InlineData("01001T", false)]  // REIT
    [InlineData("020000", false)]  // ETN
    [InlineData("2887Z1", false)]  // 特別股
    [InlineData("910322", false)]  // TDR
    [InlineData("090001", false)]
    public void IsWarrantSymbol_Tse(string symbol, bool expected)
    {
        Assert.Equal(expected, new TwseStockDataSource(new ListFetcher(), NewCache(), TimeProvider.System).IsWarrantSymbol(symbol));
    }

    [Theory]
    [InlineData("700019", true)]
    [InlineData("72479U", true)]
    [InlineData("6510", false)]
    [InlineData("8349A", false)]
    [InlineData("00679B", false)]
    [InlineData("020001", false)]
    [InlineData("01111S", false)]  // 資產基礎證券
    [InlineData("030001", false)]  // 上市權證的區段不屬於上櫃
    public void IsWarrantSymbol_Otc(string symbol, bool expected)
    {
        Assert.Equal(expected, new TpexStockDataSource(new ListFetcher(), NewCache(), TimeProvider.System).IsWarrantSymbol(symbol));
    }

    // ── 同時發生的未命中只向上游查詢一次 ──────────────────────────────────────

    /// <summary>每個請求都停在 <see cref="Release"/> 之前，模擬上游很慢（ISIN 上櫃頁實測約 24 秒）。</summary>
    private sealed class SlowFetcher(string big5Response, string jsonResponse = "") : IStockHttpFetcher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public void Release() => _release.TrySetResult();

        public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requests);
            await _release.Task.WaitAsync(ct);
            return jsonResponse;
        }

        public async Task<string> GetBig5StringAsync(string url, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requests);
            await _release.Task.WaitAsync(ct);
            return big5Response;
        }
    }

    [Fact]
    public async Task FetchStockListAsync_TenParallelFirstCalls_DownloadTheListOnce()
    {
        var fetcher = new SlowFetcher("", OpenApiFixtures.TpexQuotes);
        var source = new TpexStockDataSource(fetcher, NewCache(), TimeProvider.System);

        var callers = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => source.FetchStockListAsync()))
            .ToArray();
        while (fetcher.Requests == 0)
            await Task.Delay(10);
        await Task.Delay(100); // 讓其餘呼叫都進到快取
        fetcher.Release();
        var lists = await Task.WhenAll(callers);

        Assert.Equal(1, fetcher.Requests);
        Assert.All(lists, l => Assert.Same(lists[0], l));
        Assert.Equal(4, lists[0].Count);
    }

    [Fact]
    public async Task FetchStockListAsync_TheFirstCallerGivingUp_DoesNotAbortTheDownloadForTheOthers()
    {
        var fetcher = new SlowFetcher(IsinFixtures.Tse);
        var source = new TwseStockDataSource(fetcher, NewCache(), TimeProvider.System);
        using var impatient = new CancellationTokenSource();

        var first = source.FetchStockListAsync(Isin, impatient.Token);
        var second = source.FetchStockListAsync(Isin);
        impatient.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        fetcher.Release();

        Assert.Equal(11, (await second).Count);
        Assert.Equal(1, fetcher.Requests);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_ParallelFirstCalls_ShareOneRequest_ButNoCacheDoesNot()
    {
        var fetcher = new SlowFetcher("", Json("600.00"));
        var source = new TwseStockDataSource(fetcher, NewCache(), TimeProvider.System);

        var cached = Enumerable.Range(0, 5).Select(_ => source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default)).ToArray();
        var fresh = source.FetchRealtimeQuoteAsync("2330", QuoteOptions.NoCache);
        fetcher.Release();
        var quotes = await Task.WhenAll([.. cached, fresh]);

        Assert.Equal(2, fetcher.Requests);
        Assert.All(quotes, q => Assert.Equal(600.00m, q!.LastPrice));
    }

    // ── 批次報價 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 模擬 MIS：依 URL 的 ex_ch 每檔回一筆，<paramref name="unknown"/> 中的代號回佔位資料；
    /// 價格為 <see cref="Price"/>，每次查詢後可以改。
    /// </summary>
    private sealed class FakeMis(params string[] unknown) : IStockHttpFetcher
    {
        public List<string[]> Requests { get; } = [];
        public List<string> ExChs { get; } = [];
        public decimal Price { get; set; } = 600m;

        public Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            var exCh = Uri.UnescapeDataString(url.Split("ex_ch=")[1].Split('&')[0]);
            var symbols = exCh.Split('|').Select(e => e[4..^3]).ToArray();   // tse_2330.tw → 2330
            ExChs.Add(exCh);
            Requests.Add(symbols);

            var items = symbols.Select(s => unknown.Contains(s)
                ? """{"tv":"-","s":"-","c":"","z":"-"}"""
                : $$"""{"c":"{{s}}","n":"{{s}}","z":"{{Price.ToString(CultureInfo.InvariantCulture)}}"}""");
            return Task.FromResult($$"""{"msgArray":[{{string.Join(',', items)}}],"rtcode":"0000","rtmessage":"OK"}""");
        }

        public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_QueriesEverySymbolInOneRequest()
    {
        var mis = new FakeMis(unknown: "9999");

        var quotes = await Build(mis).FetchRealtimeQuotesAsync(["2330", "9999", "2317"], QuoteOptions.Default);

        var request = Assert.Single(mis.Requests);
        Assert.Equal(["2330", "9999", "2317"], request);
        Assert.Equal(["2317", "2330"], quotes.Keys.Order());
        Assert.Equal(600m, quotes["2330"].LastPrice);
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_SplitsLargeBatches()
    {
        var mis = new FakeMis();
        var symbols = Enumerable.Range(1000, 120).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

        var quotes = await Build(mis).FetchRealtimeQuotesAsync(symbols, QuoteOptions.Default);

        Assert.Equal([50, 50, 20], mis.Requests.Select(r => r.Length));
        Assert.Equal(120, quotes.Count);
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_SkipsCachedSymbols_AndCachesWhatItFetched()
    {
        var mis = new FakeMis();
        var source = Build(mis);
        await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        await source.FetchRealtimeQuotesAsync(["2330", "2317"], QuoteOptions.Default);
        var single = await source.FetchRealtimeQuoteAsync("2317", QuoteOptions.Default);

        Assert.Equal([["2330"], ["2317"]], mis.Requests);
        Assert.Equal(600m, single!.LastPrice);
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_NoCache_QueriesEverySymbol_AndRefreshesTheCache()
    {
        var mis = new FakeMis();
        var source = Build(mis);
        await source.FetchRealtimeQuotesAsync(["2330", "2317"], QuoteOptions.Default);
        mis.Price = 610m;

        var fresh = await source.FetchRealtimeQuotesAsync(["2330", "2317"], QuoteOptions.NoCache);
        var later = await source.FetchRealtimeQuoteAsync("2330", QuoteOptions.Default);

        Assert.Equal(2, mis.Requests.Count);
        Assert.Equal(610m, fresh["2317"].LastPrice);
        Assert.Equal(610m, later!.LastPrice);
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_ARejectedRequest_Throws()
    {
        var fetcher = new FakeFetcher("""{"rtcode":"9999","rtmessage":"參數不足"}""");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Build(fetcher).FetchRealtimeQuotesAsync(["2330", "2317"], QuoteOptions.Default));
    }

    [Fact]
    public async Task FetchRealtimeQuotesAsync_UsesTheMarketPrefix()
    {
        var mis = new FakeMis();
        var otc = new TpexStockDataSource(mis, new MemoryCacheService(new MemoryCache(new MemoryCacheOptions())), TimeProvider.System);

        var quotes = await otc.FetchRealtimeQuotesAsync(["6510", "8069"], QuoteOptions.Default);

        Assert.Equal("otc_6510.tw|otc_8069.tw", Assert.Single(mis.ExChs));
        Assert.All(quotes.Values, q => Assert.Equal(MarketType.OTC, q.Market));
    }

    // ── 代號跳脫 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Symbols_AreEscapedInUpstreamUrls()
    {
        // 正常情況下 StockMarketService 已擋下這類代號；資料來源本身也不讓它改到其他查詢參數
        var fetcher = new FakeFetcher("""{"msgArray":[],"stat":"OK","data":[],"tables":[]}""");
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));

        await new TwseStockDataSource(fetcher, cache, TimeProvider.System)
            .FetchRealtimeQuoteAsync("2330&delay=1#", QuoteOptions.NoCache);
        await new TwseStockDataSource(fetcher, cache, TimeProvider.System)
            .FetchHistoricalDataAsync("2330&date=1", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));
        await new TpexStockDataSource(fetcher, cache, TimeProvider.System)
            .FetchHistoricalDataAsync("6510&date=1", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

        Assert.Contains("ex_ch=tse_2330%26delay%3D1%23.tw&", fetcher.Urls[0]);
        Assert.Contains("stockNo=2330%26date%3D1", fetcher.Urls[1]);
        Assert.Contains("code=6510%26date%3D1", fetcher.Urls[2]);
    }

    // ── 歷史資料的快取時間 ────────────────────────────────────────────────────

    private sealed class RecordingCache : ICacheService
    {
        public Dictionary<string, TimeSpan> Expiries { get; } = [];

        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry)
        {
            Expiries[key] = expiry;
            return factory();
        }

        public Task<T> RefreshAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry)
            => GetOrSetAsync(key, factory, expiry);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Theory]
    [InlineData("2025-09-15T10:00:00+08:00")]
    [InlineData("2025-08-31T17:00:00+00:00")] // UTC 還在 8 月，台灣時間已是 9/1 凌晨
    public async Task FetchHistoricalDataAsync_OnlyClosedMonthsAreCachedForADay(string now)
    {
        var cache = new RecordingCache();
        var source = new TwseStockDataSource(
            new FakeFetcher("""{"stat":"OK","data":[]}"""), cache,
            new FixedTimeProvider(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));

        await source.FetchHistoricalDataAsync("2330", new DateOnly(2025, 8, 1), new DateOnly(2025, 10, 31));

        Assert.Equal(TimeSpan.FromDays(1), cache.Expiries["history_TSE_2330_202508"]);
        Assert.True(cache.Expiries["history_TSE_2330_202509"] < TimeSpan.FromHours(1), "當月只能短暫快取");
        Assert.True(cache.Expiries["history_TSE_2330_202510"] < TimeSpan.FromHours(1), "未來月份只能短暫快取");
    }
}
