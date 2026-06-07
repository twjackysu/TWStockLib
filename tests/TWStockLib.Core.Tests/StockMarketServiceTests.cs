using Microsoft.Extensions.Logging.Abstractions;

namespace TWStockLib.Core.Tests;

public class StockMarketServiceTests
{
    private static IStockDataSource FakeSource(MarketType market)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        return source;
    }

    private static StockMarketService BuildService(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance);

    // ── 路由 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_UnknownMarket_ReturnsSourceNotFound()
    {
        var service = BuildService(FakeSource(MarketType.TSE));

        var result = await service.GetRealtimeQuoteAsync("2330", MarketType.OTC);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_RoutesToMatchingMarket()
    {
        var tse = FakeSource(MarketType.TSE);
        var otc = FakeSource(MarketType.OTC);
        var quote = new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m };
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<CancellationToken>()).Returns(quote);

        var result = await BuildService(tse, otc).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        Assert.Same(quote, result.Value);
        await otc.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── 成功 / 失敗收斂 ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_SourceReturnsNull_ReturnsNotFound()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns((StockQuote?)null);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("9999", MarketType.TSE);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_SourceThrows_ReturnsUpstreamError()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns<StockQuote?>(_ => throw new HttpRequestException("boom"));

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.UpstreamError, result.ErrorCode);
        Assert.Contains("boom", result.ErrorMessage);
    }

    // ── GetAllStockList 合併 ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllStockListAsync_MergesAllSources()
    {
        var tse = FakeSource(MarketType.TSE);
        var otc = FakeSource(MarketType.OTC);
        tse.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
           .Returns(new Dictionary<string, StockData>
           {
               ["2330"] = new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE },
           });
        otc.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
           .Returns(new Dictionary<string, StockData>
           {
               ["6510"] = new() { Symbol = "6510", Name = "精測", Market = MarketType.OTC },
           });

        var result = await BuildService(tse, otc).GetAllStockListAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains("2330", result.Value.Keys);
        Assert.Contains("6510", result.Value.Keys);
    }

    [Fact]
    public async Task GetStockListAsync_UnknownMarket_ReturnsSourceNotFound()
    {
        var result = await BuildService(FakeSource(MarketType.TSE))
            .GetStockListAsync(MarketType.OTC);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }
}
