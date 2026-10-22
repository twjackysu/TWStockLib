using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TWStockLib.Abstractions;
using TWStockLib.Twse.Http;

namespace TWStockLib.Twse.Tests;

public class TwStockOptionsTests
{
    [Fact]
    public void Defaults_AreTheCacheDurationsHardCodedBefore31()
    {
        var options = new TwStockOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.QuoteCacheDuration);
        Assert.Equal(TimeSpan.FromDays(1), options.StockListCacheDuration);
        Assert.Equal(TimeSpan.FromDays(1), options.ClosedMonthHistoryCacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(30), options.CurrentMonthHistoryCacheDuration);
    }

    // ── 經由 AddTwStock 實際套用到資料來源 ────────────────────────────────────

    private sealed class FakeFetcher : IStockHttpFetcher
    {
        public Task<string> GetStringAsync(string url, CancellationToken ct = default) => Task.FromResult(
            url.Contains("getStockInfo", StringComparison.Ordinal)
                ? """{"msgArray":[{"c":"2330","n":"台積電","z":"600.00","y":"596.00","tlong":"1699000000000"}]}"""
                : url.Contains("openapi", StringComparison.Ordinal)
                    ? """[{"Code":"2330","Name":"台積電"}]"""
                    : """{"stat":"OK","data":[]}""");

        public Task<string> GetBig5StringAsync(string url, CancellationToken ct = default) => Task.FromResult("<html></html>");
    }

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

    /// <summary>以 2025-09-15（台灣時間）查詢報價、8～9 月歷史與清單，回傳各快取鍵用到的到期時間。</summary>
    private static async Task<Dictionary<string, TimeSpan>> RecordExpiries(Action<IServiceCollection> addTwStock)
    {
        var cache = new RecordingCache();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ICacheService>(cache);
        services.AddSingleton<IStockHttpFetcher>(new FakeFetcher());
        services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(DateTimeOffset.Parse("2025-09-15T10:00:00+08:00", CultureInfo.InvariantCulture)));
        addTwStock(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var stock = provider.GetRequiredService<IStockMarketService>();

        Assert.True((await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE)).IsSuccess);
        Assert.True((await stock.GetHistoricalDataAsync(
            "2330", MarketType.TSE, new DateOnly(2025, 8, 1), new DateOnly(2025, 9, 30))).IsSuccess);
        Assert.True((await stock.GetStockListAsync(MarketType.TSE)).IsSuccess);

        return cache.Expiries;
    }

    [Fact]
    public async Task AddTwStock_WithoutOptions_KeepsTheDefaultCacheDurations()
    {
        var expiries = await RecordExpiries(services => services.AddTwStock());

        Assert.Equal(TimeSpan.FromSeconds(30), expiries["realtime_quote_TSE_2330"]);
        Assert.Equal(TimeSpan.FromDays(1), expiries["history_TSE_2330_202508"]);
        Assert.Equal(TimeSpan.FromMinutes(30), expiries["history_TSE_2330_202509"]);
        Assert.Equal(TimeSpan.FromDays(1), expiries["stock_list_openapi_TSE"]);
    }

    [Fact]
    public async Task AddTwStock_WithOptions_OverridesTheCacheDurations()
    {
        var expiries = await RecordExpiries(services => services.AddTwStock(o =>
        {
            o.QuoteCacheDuration = TimeSpan.FromSeconds(10);
            o.ClosedMonthHistoryCacheDuration = TimeSpan.FromDays(7);
            o.CurrentMonthHistoryCacheDuration = TimeSpan.FromMinutes(5);
            o.StockListCacheDuration = TimeSpan.FromHours(6);
        }));

        Assert.Equal(TimeSpan.FromSeconds(10), expiries["realtime_quote_TSE_2330"]);
        Assert.Equal(TimeSpan.FromDays(7), expiries["history_TSE_2330_202508"]);
        Assert.Equal(TimeSpan.FromMinutes(5), expiries["history_TSE_2330_202509"]);
        Assert.Equal(TimeSpan.FromHours(6), expiries["stock_list_openapi_TSE"]);
    }

    [Fact]
    public async Task AddTwStock_CalledWithOptionsMoreThanOnce_AppliesEachConfigureInOrder()
    {
        var expiries = await RecordExpiries(services =>
        {
            services.AddTwStock(o => o.QuoteCacheDuration = TimeSpan.FromSeconds(10));
            services.AddTwStock(o => o.StockListCacheDuration = TimeSpan.FromHours(6));
            services.AddTwStock(o => o.QuoteCacheDuration = TimeSpan.FromSeconds(20));
        });

        Assert.Equal(TimeSpan.FromSeconds(20), expiries["realtime_quote_TSE_2330"]);
        Assert.Equal(TimeSpan.FromHours(6), expiries["stock_list_openapi_TSE"]);
    }

    [Fact]
    public void AddTwStock_ANonPositiveCacheDuration_FailsWhenTheServiceIsResolved()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddTwStock(o => o.QuoteCacheDuration = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStockMarketService>());
        Assert.Contains(nameof(TwStockOptions.QuoteCacheDuration), ex.Message);
    }

    [Fact]
    public void RateLimitDefaults_AreThreeRequestsPerFiveSecondsPerHost()
    {
        var rateLimit = new TwStockOptions().RateLimit;

        Assert.True(rateLimit.Enabled);
        Assert.Equal(3, rateLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(5), rateLimit.Window);
    }

    [Fact]
    public void AddTwStock_AZeroPermitLimit_FailsWhenTheServiceIsResolved()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddTwStock(o => o.RateLimit.PermitLimit = 0);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStockMarketService>());
        Assert.Contains("RateLimit.PermitLimit", ex.Message);
    }
}
