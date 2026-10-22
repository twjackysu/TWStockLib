using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;

namespace TWStockLib.Core.Tests;

public class GetRealtimeQuotesTests
{
    private static StockQuote Quote(string symbol, MarketType market, decimal lastPrice = 100m)
        => new() { Symbol = symbol, Name = symbol, Market = market, LastPrice = lastPrice };

    /// <summary>批次查詢回傳 <paramref name="known"/> 中有的代號。</summary>
    private static IStockDataSource BatchSource(MarketType market, params string[] known)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns(ci => (IReadOnlyDictionary<string, StockQuote>)ci.Arg<IReadOnlyList<string>>()
                  .Where(known.Contains)
                  .ToFrozenDictionary(s => s, s => Quote(s, market)));
        return source;
    }

    private static StockMarketService BuildService(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance);

    [Fact]
    public async Task OneBatchPerMarket_WithEverySymbolOfThatMarket()
    {
        var tse = BatchSource(MarketType.TSE, "2330", "2317");
        var otc = BatchSource(MarketType.OTC, "6510");

        var results = await BuildService(tse, otc).GetRealtimeQuotesAsync(
            [("2330", MarketType.TSE), ("6510", MarketType.OTC), ("2317", MarketType.TSE)]);

        Assert.Equal(3, results.Count);
        Assert.All(results.Values, r => Assert.True(r.IsSuccess));
        Assert.Equal(MarketType.OTC, results["6510"].Value!.Market);
        await tse.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "2330", "2317" })), QuoteOptions.Default, Arg.Any<CancellationToken>());
        await otc.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "6510" })), QuoteOptions.Default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASymbolTheSourceDidNotReturn_IsNotFound()
    {
        var tse = BatchSource(MarketType.TSE, "2330");

        var results = await BuildService(tse).GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("9999", MarketType.TSE)]);

        Assert.True(results["2330"].IsSuccess);
        Assert.Equal(StockErrorCodes.NotFound, results["9999"].ErrorCode);
        Assert.Contains("9999", results["9999"].ErrorMessage);
    }

    [Fact]
    public async Task AnUnsupportedMarket_FailsOnlyItsOwnSymbols()
    {
        var results = await BuildService(BatchSource(MarketType.TSE, "2330"))
            .GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC)]);

        Assert.True(results["2330"].IsSuccess);
        Assert.Equal(StockErrorCodes.SourceNotFound, results["6510"].ErrorCode);
    }

    [Fact]
    public async Task AFailingMarket_FailsEachOfItsSymbols_WithTheCause()
    {
        var tse = BatchSource(MarketType.TSE, "2330");
        var otc = Substitute.For<IStockDataSource>();
        otc.Market.Returns(MarketType.OTC);
        var cause = new HttpRequestException("boom");
        otc.FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
           .Returns<IReadOnlyDictionary<string, StockQuote>>(_ => throw cause);

        var results = await BuildService(tse, otc).GetRealtimeQuotesAsync(
            [("2330", MarketType.TSE), ("6510", MarketType.OTC), ("8069", MarketType.OTC)]);

        Assert.True(results["2330"].IsSuccess);
        Assert.All(new[] { "6510", "8069" }, symbol =>
        {
            Assert.Equal(StockErrorCodes.UpstreamError, results[symbol].ErrorCode);
            Assert.Same(cause, results[symbol].Exception);
        });
    }

    [Fact]
    public async Task DuplicateSymbols_AreQueriedOnce()
    {
        var tse = BatchSource(MarketType.TSE, "2330");

        var results = await BuildService(tse).GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("2330", MarketType.TSE)]);

        Assert.Single(results);
        await tse.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.Count == 1), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoCache_IsPassedToTheSource()
    {
        var tse = BatchSource(MarketType.TSE, "2330");

        await BuildService(tse).GetRealtimeQuotesAsync([("2330", MarketType.TSE)], QuoteOptions.NoCache);

        await tse.Received(1).FetchRealtimeQuotesAsync(
            Arg.Any<IReadOnlyList<string>>(), QuoteOptions.NoCache, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationTokenOnlyOverload_UsesTheDefaults()
    {
        var tse = BatchSource(MarketType.TSE, "2330");
        using var cts = new CancellationTokenSource();

        await BuildService(tse).GetRealtimeQuotesAsync([("2330", MarketType.TSE)], cts.Token);

        await tse.Received(1).FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), QuoteOptions.Default, cts.Token);
    }

    [Fact]
    public async Task NoSymbols_ReturnsAnEmptyResult_WithoutQuerying()
    {
        var tse = BatchSource(MarketType.TSE);

        var results = await BuildService(tse).GetRealtimeQuotesAsync(Array.Empty<(string, MarketType)>());

        Assert.Empty(results);
        await tse.DidNotReceiveWithAnyArgs().FetchRealtimeQuotesAsync(default!, default!, default);
    }

    [Fact]
    public void InvalidArguments_AreRejectedWhenCalled()
    {
        var service = BuildService(BatchSource(MarketType.TSE));

        Assert.Throws<ArgumentNullException>(() => { _ = service.GetRealtimeQuotesAsync((IEnumerable<(string, MarketType)>)null!); });
        Assert.Throws<ArgumentException>(() => { _ = service.GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("2330.tw|tse_2317", MarketType.TSE)]); });
        Assert.Throws<ArgumentException>(() => { _ = service.GetRealtimeQuotesAsync([("2330", MarketType.TSE), ("2330", MarketType.OTC)]); });
    }

    [Fact]
    public async Task CallerCancellation_IsThrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tse = Substitute.For<IStockDataSource>();
        tse.Market.Returns(MarketType.TSE);
        tse.FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
           .Returns<IReadOnlyDictionary<string, StockQuote>>(ci => throw new OperationCanceledException(ci.Arg<CancellationToken>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildService(tse).GetRealtimeQuotesAsync([("2330", MarketType.TSE)], ct: cts.Token));
    }

    // ── IStockDataSource 的預設實作 ──────────────────────────────────────────

    /// <summary>只實作 3.0 既有成員的第三方來源：批次查詢走介面的預設實作。</summary>
    private sealed class SingleQuoteOnlySource : IStockDataSource
    {
        public List<(string Symbol, QuoteOptions Options)> Calls { get; } = [];

        public MarketType Market => MarketType.TSE;

        public Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default)
        {
            Calls.Add((symbol, options));
            return Task.FromResult(symbol == "9999" ? null : Quote(symbol, Market));
        }

        public Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
            string symbol, DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
            bool includeWarrant = false, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ASourceWithoutBatchSupport_IsQueriedOneSymbolAtATime()
    {
        var source = new SingleQuoteOnlySource();

        var results = await BuildService(source).GetRealtimeQuotesAsync(
            [("2330", MarketType.TSE), ("9999", MarketType.TSE), ("2317", MarketType.TSE)], QuoteOptions.NoCache);

        Assert.Equal([("2330", QuoteOptions.NoCache), ("9999", QuoteOptions.NoCache), ("2317", QuoteOptions.NoCache)], source.Calls);
        Assert.True(results["2330"].IsSuccess);
        Assert.True(results["2317"].IsSuccess);
        Assert.Equal(StockErrorCodes.NotFound, results["9999"].ErrorCode);
    }
}
