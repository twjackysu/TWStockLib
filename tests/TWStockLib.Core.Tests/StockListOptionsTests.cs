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

    [Fact]
    public void WithWarrants_IsOpenApiIncludingWarrants()
    {
        Assert.Equal(StockListSource.OpenApi, StockListOptions.WithWarrants.Source);
        Assert.True(StockListOptions.WithWarrants.IncludeWarrant);
    }

    [Fact]
    public async Task WithoutOptions_TheDefaultIsUsed()
    {
        var tse = Source(MarketType.TSE, "2330");
        var service = BuildService(tse);
        using var cts = new CancellationTokenSource();

        await service.GetStockListAsync(MarketType.TSE);
        await service.GetAllStockListAsync();
        await service.GetStockListAsync(MarketType.TSE, cts.Token);   // 只傳 token 的擴充多載
        await service.GetAllStockListAsync(cts.Token);

        await tse.Received(4).FetchStockListAsync(StockListOptions.Default, Arg.Any<CancellationToken>());
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

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetStockListAsync(MarketType.TSE, undefined); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetAllStockListAsync(undefined); });
        tse.DidNotReceiveWithAnyArgs().FetchStockListAsync(default(StockListOptions)!, default);
    }
}
