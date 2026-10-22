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

    private static TwseStockDataSource Build(FakeFetcher fetcher) =>
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

    private sealed class IsinFetcher(string html) : IStockHttpFetcher
    {
        public List<string> Urls { get; } = [];

        public Task<string> GetStringAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default)
        {
            lock (Urls)
                Urls.Add(url);
            return Task.FromResult(html);
        }
    }

    private static ICacheService NewCache() => new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task FetchStockListAsync_WithAndWithoutWarrants_ShareOneDownload()
    {
        var fetcher = new IsinFetcher(IsinFixtures.Tse);
        var source = new TwseStockDataSource(fetcher, NewCache(), TimeProvider.System);

        var withoutWarrants = await source.FetchStockListAsync(includeWarrant: false);
        var all = await source.FetchStockListAsync(includeWarrant: true);

        Assert.Equal(11, withoutWarrants.Count);
        Assert.Equal(15, all.Count);
        Assert.Equal(["https://isin.twse.com.tw/isin/C_public.jsp?strMode=2"], fetcher.Urls);
    }

    [Fact]
    public async Task FetchStockListAsync_ByDefault_ExcludesWarrants_IncludingBullBearCertificates()
    {
        var source = new TwseStockDataSource(new IsinFetcher(IsinFixtures.Tse), NewCache(), TimeProvider.System);

        var list = await source.FetchStockListAsync();

        Assert.DoesNotContain(list.Values, s => s.SecurityType == SecurityType.Warrant);
        Assert.DoesNotContain("03989C", list.Keys); // 牛證
        Assert.DoesNotContain("03726B", list.Keys); // 熊證
        Assert.Contains("0050", list.Keys);
        Assert.True(((ICollection<KeyValuePair<string, StockData>>)list).IsReadOnly);
    }

    [Fact]
    public async Task FetchStockListAsync_WithoutWarrants_IsFilteredOncePerDownload()
    {
        var source = new TwseStockDataSource(new IsinFetcher(IsinFixtures.Tse), NewCache(), TimeProvider.System);

        var first = await source.FetchStockListAsync();
        var second = await source.FetchStockListAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task FetchStockListAsync_Otc_UsesItsOwnPageAndCacheEntry()
    {
        var cache = NewCache();
        var tseFetcher = new IsinFetcher(IsinFixtures.Tse);
        var otcFetcher = new IsinFetcher(IsinFixtures.Otc);
        await new TwseStockDataSource(tseFetcher, cache, TimeProvider.System).FetchStockListAsync();

        var otc = await new TpexStockDataSource(otcFetcher, cache, TimeProvider.System).FetchStockListAsync(includeWarrant: true);

        Assert.Equal(["https://isin.twse.com.tw/isin/C_public.jsp?strMode=4"], otcFetcher.Urls);
        Assert.Equal(7, otc.Count);
        Assert.All(otc.Values, s => Assert.Equal(MarketType.OTC, s.Market));
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
    public async Task FetchStockListAsync_TenParallelFirstCalls_DownloadThePageOnce()
    {
        var fetcher = new SlowFetcher(IsinFixtures.Tse);
        var source = new TwseStockDataSource(fetcher, NewCache(), TimeProvider.System);

        var callers = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => source.FetchStockListAsync(includeWarrant: i % 2 == 0)))
            .ToArray();
        while (fetcher.Requests == 0)
            await Task.Delay(10);
        await Task.Delay(100); // 讓其餘呼叫都進到快取
        fetcher.Release();
        var lists = await Task.WhenAll(callers);

        Assert.Equal(1, fetcher.Requests);
        Assert.All(lists.Where((_, i) => i % 2 == 0), l => Assert.Equal(15, l.Count));
        Assert.All(lists.Where((_, i) => i % 2 == 1), l => Assert.Equal(11, l.Count));
    }

    [Fact]
    public async Task FetchStockListAsync_TheFirstCallerGivingUp_DoesNotAbortTheDownloadForTheOthers()
    {
        var fetcher = new SlowFetcher(IsinFixtures.Tse);
        var source = new TwseStockDataSource(fetcher, NewCache(), TimeProvider.System);
        using var impatient = new CancellationTokenSource();

        var first = source.FetchStockListAsync(ct: impatient.Token);
        var second = source.FetchStockListAsync();
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
