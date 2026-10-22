using Microsoft.Extensions.Logging.Abstractions;

namespace TWStockLib.Core.Tests;

public class StockListFilterTests
{
    private static StockData Stock(string symbol, SecurityType type, MarketType market = MarketType.TSE)
        => new() { Symbol = symbol, Name = symbol, Market = market, SecurityType = type };

    /// <summary>模擬內建來源：依 includeWarrant 從同一份完整清單過濾。</summary>
    private static IStockDataSource Source(MarketType market, params StockData[] stocks)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns(ci => (IReadOnlyDictionary<string, StockData>)stocks
                  .Where(s => ci.Arg<bool>() || s.SecurityType != SecurityType.Warrant)
                  .ToDictionary(s => s.Symbol));
        return source;
    }

    private static StockMarketService BuildService(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance);

    private static readonly StockData[] TseStocks =
    [
        Stock("2330", SecurityType.CommonStock),
        Stock("2887Z1", SecurityType.PreferredStock),
        Stock("0050", SecurityType.Etf),
        Stock("020000", SecurityType.Etn),
        Stock("030001", SecurityType.Warrant),
        Stock("9999", SecurityType.Unknown),
    ];

    [Fact]
    public async Task GetStockListAsync_BySecurityTypes_ReturnsOnlyThoseTypes()
    {
        var tse = Source(MarketType.TSE, TseStocks);

        var result = await BuildService(tse).GetStockListAsync(MarketType.TSE, [SecurityType.CommonStock, SecurityType.Etf]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["0050", "2330"], result.Value!.Keys.Order());
        Assert.True(((ICollection<KeyValuePair<string, StockData>>)result.Value).IsReadOnly);
        await tse.Received(1).FetchStockListAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStockListAsync_AskingForWarrants_UsesTheFullList()
    {
        var tse = Source(MarketType.TSE, TseStocks);

        var result = await BuildService(tse).GetStockListAsync(MarketType.TSE, [SecurityType.Warrant]);

        Assert.Equal(["030001"], result.Value!.Keys);
        await tse.Received(1).FetchStockListAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStockListAsync_UnclassifiedEntries_OnlyWhenUnknownIsRequested()
    {
        var service = BuildService(Source(MarketType.TSE, TseStocks));

        var stocks = await service.GetStockListAsync(MarketType.TSE, [SecurityType.CommonStock]);
        var unknown = await service.GetStockListAsync(MarketType.TSE, [SecurityType.Unknown]);

        Assert.DoesNotContain("9999", stocks.Value!.Keys);
        Assert.Equal(["9999"], unknown.Value!.Keys);
    }

    [Fact]
    public async Task GetStockListAsync_NoTypes_ReturnsAnEmptyList()
    {
        var result = await BuildService(Source(MarketType.TSE, TseStocks)).GetStockListAsync(MarketType.TSE, []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetStockListAsync_ByType_PassesFailuresThrough()
    {
        var result = await BuildService(Source(MarketType.TSE, TseStocks))
            .GetStockListAsync(MarketType.OTC, [SecurityType.Etf]);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }

    [Fact]
    public void GetStockListAsync_NullTypes_ThrowsImmediately()
    {
        var service = BuildService(Source(MarketType.TSE, TseStocks));

        Assert.Throws<ArgumentNullException>(() => { _ = service.GetStockListAsync(MarketType.TSE, (IReadOnlyCollection<SecurityType>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = service.GetAllStockListAsync((IReadOnlyCollection<SecurityType>)null!); });
    }

    [Fact]
    public async Task GetAllStockListAsync_BySecurityTypes_FiltersEveryMarket()
    {
        var service = BuildService(
            Source(MarketType.TSE, TseStocks),
            Source(MarketType.OTC, Stock("6510", SecurityType.CommonStock, MarketType.OTC), Stock("00679B", SecurityType.Etf, MarketType.OTC)));

        var result = await service.GetAllStockListAsync([SecurityType.Etf]);

        Assert.Equal(["0050", "00679B"], result.Value!.Keys.Order());
    }
}
