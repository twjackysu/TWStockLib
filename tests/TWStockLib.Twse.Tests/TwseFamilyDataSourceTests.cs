using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Cache;
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

        var first = await source.FetchRealtimeQuoteAsync("2330");
        var second = await source.FetchRealtimeQuoteAsync("2330");

        Assert.Equal(600.00m, first!.LastPrice);
        Assert.Same(first, second);
        Assert.Single(fetcher.Urls);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_BypassCache_HitsTheUpstreamAgain()
    {
        var fetcher = new FakeFetcher(Json("600.00"), Json("610.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330");

        var fresh = await source.FetchRealtimeQuoteAsync("2330", bypassCache: true);

        Assert.Equal(610.00m, fresh!.LastPrice);
        Assert.Equal(2, fetcher.Urls.Count);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_AfterABypass_NormalCallsSeeTheNewerQuote()
    {
        var fetcher = new FakeFetcher(Json("600.00"), Json("610.00"), Json("620.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330");
        await source.FetchRealtimeQuoteAsync("2330", bypassCache: true);

        var later = await source.FetchRealtimeQuoteAsync("2330");

        Assert.Equal(610.00m, later!.LastPrice);
        Assert.Equal(2, fetcher.Urls.Count);
    }

    [Fact]
    public async Task FetchRealtimeQuoteAsync_Bypass_DoesNotTouchOtherSymbols()
    {
        var fetcher = new FakeFetcher(Json("600.00"));
        var source = Build(fetcher);
        await source.FetchRealtimeQuoteAsync("2330");

        await source.FetchRealtimeQuoteAsync("2317", bypassCache: true);
        await source.FetchRealtimeQuoteAsync("2330");

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

        var quote = await source.FetchRealtimeQuoteAsync("t00", bypassCache: true);

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

        var quote = await Build(fetcher).FetchRealtimeQuoteAsync("t00", bypassCache: true);

        Assert.Null(quote!.LastPrice);
        Assert.Equal(22950.00m, quote.YesterdayClosingPrice);
    }

    // ── 歷史資料的快取時間 ────────────────────────────────────────────────────

    private sealed class RecordingCache : ICacheService
    {
        public Dictionary<string, TimeSpan?> Expiries { get; } = [];

        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            Expiries[key] = expiry;
            return factory();
        }
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
