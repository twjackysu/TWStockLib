using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;

namespace TWStockLib.Core.Tests;

/// <summary>股票清單的 <see cref="StockListOptions"/>：選擇上游、是否含權證。</summary>
public class StockListOptionsTests
{
    private static IStockDataSource Source(MarketType market, params string[] symbols)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
              .Returns(symbols.ToFrozenDictionary(s => s, s => new StockData { Symbol = s, Name = s, Market = market }));
        return source;
    }

    private static StockMarketService BuildService(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance);

    [Fact]
    public void Default_IsOpenApiWithoutWarrants()
    {
        Assert.Equal(StockListSource.OpenApi, StockListOptions.Default.Source);
        Assert.False(StockListOptions.Default.IncludeWarrant);
        Assert.Equal(StockListSource.OpenApi, default(StockListSource));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheBoolOverloads_UseTheDefaultSource(bool includeWarrant)
    {
        var tse = Source(MarketType.TSE, "2330");
        var service = BuildService(tse);

        await service.GetStockListAsync(MarketType.TSE, includeWarrant);
        await service.GetAllStockListAsync(includeWarrant);

        await tse.Received(2).FetchStockListAsync(
            new StockListOptions { IncludeWarrant = includeWarrant, Source = StockListSource.OpenApi }, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOptions_ArePassedToTheSource()
    {
        var tse = Source(MarketType.TSE, "2330");
        var options = new StockListOptions { IncludeWarrant = true, Source = StockListSource.Isin };

        var result = await BuildService(tse).GetStockListAsync(MarketType.TSE, options);

        Assert.True(result.IsSuccess);
        await tse.Received(1).FetchStockListAsync(options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllStockListAsync_PassesTheOptionsToEveryMarket()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510");
        var options = new StockListOptions { Source = StockListSource.Isin };

        var result = await BuildService(tse, otc).GetAllStockListAsync(options);

        Assert.Equal(["2330", "6510"], result.Value!.Keys.Order());
        await tse.Received(1).FetchStockListAsync(options, Arg.Any<CancellationToken>());
        await otc.Received(1).FetchStockListAsync(options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InvalidOptions_ThrowImmediately()
    {
        var tse = Source(MarketType.TSE, "2330");
        var service = BuildService(tse);
        var undefined = new StockListOptions { Source = (StockListSource)42 };

        Assert.Throws<ArgumentNullException>(() => { _ = service.GetStockListAsync(MarketType.TSE, (StockListOptions)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = service.GetAllStockListAsync((StockListOptions)null!); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetStockListAsync(MarketType.TSE, undefined); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetAllStockListAsync(undefined); });
        tse.DidNotReceiveWithAnyArgs().FetchStockListAsync(default(StockListOptions)!, default);
    }

    /// <summary>只實作 bool 多載的第三方來源（3.0 的寫法）。</summary>
    private sealed class LegacySource : IStockDataSource
    {
        public List<bool> Requests { get; } = [];
        public MarketType Market => MarketType.TSE;

        public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default)
            => Task.FromResult<StockQuote?>(null);

        public Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
            string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StockHistory>>([]);

        public Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false, CancellationToken ct = default)
        {
            Requests.Add(includeWarrant);
            return Task.FromResult<IReadOnlyDictionary<string, StockData>>(new Dictionary<string, StockData>
            {
                ["2330"] = new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE },
            });
        }
    }

    [Fact]
    public async Task ASourceWithoutTheOptionsOverload_GetsIncludeWarrant_AndIgnoresTheUpstreamChoice()
    {
        var legacy = new LegacySource();
        var service = BuildService(legacy);

        var result = await service.GetStockListAsync(MarketType.TSE, new StockListOptions { IncludeWarrant = true, Source = StockListSource.Isin });
        await service.GetStockListAsync(MarketType.TSE);

        Assert.True(result.IsSuccess);
        Assert.Equal([true, false], legacy.Requests);
    }
}
