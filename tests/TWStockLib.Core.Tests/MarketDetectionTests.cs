using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace TWStockLib.Core.Tests;

/// <summary>不指定市場的多載：依股票清單判斷上市或上櫃。</summary>
public class MarketDetectionTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();

    private static StockQuote Quote(string symbol, MarketType market)
        => new() { Symbol = symbol, Name = symbol, Market = market, LastPrice = 100m };

    /// <summary>股票清單為 <paramref name="listed"/>，報價（單檔與批次）只查得到清單中的代號。</summary>
    private static IStockDataSource Source(MarketType market, params string[] listed)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns(listed.ToFrozenDictionary(s => s, s => new StockData { Symbol = s, Name = s, Market = market }));
        source.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns(ci => listed.Contains(ci.Arg<string>()) ? Quote(ci.Arg<string>(), market) : null);
        source.FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns(ci => (IReadOnlyDictionary<string, StockQuote>)ci.Arg<IReadOnlyList<string>>()
                  .Where(listed.Contains)
                  .ToFrozenDictionary(s => s, s => Quote(s, market)));
        source.FetchHistoricalDataAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
              .Returns(Array.Empty<StockHistory>());
        return source;
    }

    /// <summary>股票清單取得失敗的來源。</summary>
    private static IStockDataSource FailingListSource(MarketType market)
    {
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns<IReadOnlyDictionary<string, StockData>>(_ => throw new HttpRequestException("isin down"));
        return source;
    }

    private StockMarketService Service(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance, _time);

    // ── 單檔報價 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AListedSymbol_IsQueriedInItsMarket_WithoutDownloadingTheOtherLists()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510");

        var result = await Service(tse, otc).GetRealtimeQuoteAsync("2330", QuoteOptions.NoCache);

        Assert.Equal(MarketType.TSE, result.GetValueOrThrow().Market);
        await tse.Received(1).FetchRealtimeQuoteAsync("2330", QuoteOptions.NoCache, Arg.Any<CancellationToken>());
        await tse.Received(1).FetchStockListAsync(false, Arg.Any<CancellationToken>());
        await otc.DidNotReceiveWithAnyArgs().FetchStockListAsync(default, default);
    }

    [Fact]
    public async Task AnOtcSymbol_IsFoundInTheOtcList()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510");

        var result = await Service(tse, otc).GetRealtimeQuoteAsync("6510");

        Assert.Equal(MarketType.OTC, result.GetValueOrThrow().Market);
        await tse.DidNotReceiveWithAnyArgs().FetchRealtimeQuoteAsync(default!, default!, default);
    }

    [Fact]
    public async Task ASymbolInNoList_IsNotFound_WithoutQueryingAQuote()
    {
        var tse = Source(MarketType.TSE, "2330");

        var result = await Service(tse, Source(MarketType.OTC)).GetRealtimeQuoteAsync("9999");

        Assert.Equal(StockErrorCodes.NotFound, result.ErrorCode);
        Assert.Contains("9999", result.ErrorMessage);
        await tse.DidNotReceiveWithAnyArgs().FetchRealtimeQuoteAsync(default!, default!, default);
    }

    [Fact]
    public async Task AFailingList_DoesNotHideASymbolInAnotherList()
    {
        var result = await Service(FailingListSource(MarketType.TSE), Source(MarketType.OTC, "6510"))
            .GetRealtimeQuoteAsync("6510");

        Assert.Equal(MarketType.OTC, result.GetValueOrThrow().Market);
    }

    [Fact]
    public async Task AFailingList_IsReportedInsteadOfNotFound()
    {
        // 找不到可能只是因為它在那份沒取得的清單裡
        var result = await Service(FailingListSource(MarketType.TSE), Source(MarketType.OTC, "6510"))
            .GetRealtimeQuoteAsync("2330");

        Assert.Equal(StockErrorCodes.UpstreamError, result.ErrorCode);
        Assert.Contains("isin down", result.ErrorMessage);
    }

    [Fact]
    public async Task TheCancellationTokenOnlyOverload_UsesTheDefaults()
    {
        var tse = Source(MarketType.TSE, "2330");
        using var cts = new CancellationTokenSource();

        await Service(tse).GetRealtimeQuoteAsync("2330", cts.Token);

        await tse.Received(1).FetchRealtimeQuoteAsync("2330", QuoteOptions.Default, cts.Token);
    }

    // ── 歷史資料 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task History_IsQueriedInTheDetectedMarket()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510");
        var (from, to) = (new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

        var result = await Service(tse, otc).GetHistoricalDataAsync("6510", from, to);

        Assert.True(result.IsSuccess);
        await otc.Received(1).FetchHistoricalDataAsync("6510", from, to, Arg.Any<CancellationToken>());
        await tse.DidNotReceiveWithAnyArgs().FetchHistoricalDataAsync(default!, default, default, default);
    }

    // ── 批次報價 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Batch_GroupsTheDetectedMarkets_AndReportsUnknownSymbols()
    {
        var tse = Source(MarketType.TSE, "2330", "2317");
        var otc = Source(MarketType.OTC, "6510");

        var results = await Service(tse, otc).GetRealtimeQuotesAsync(["2330", "6510", "9999", "2317"]);

        Assert.Equal(MarketType.TSE, results["2330"].GetValueOrThrow().Market);
        Assert.Equal(MarketType.TSE, results["2317"].GetValueOrThrow().Market);
        Assert.Equal(MarketType.OTC, results["6510"].GetValueOrThrow().Market);
        Assert.Equal(StockErrorCodes.NotFound, results["9999"].ErrorCode);
        await tse.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "2330", "2317" })), QuoteOptions.Default, Arg.Any<CancellationToken>());
        await otc.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "6510" })), QuoteOptions.Default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Batch_EveryListIsFetchedOnce()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510", "8069");

        await Service(tse, otc).GetRealtimeQuotesAsync(["6510", "2330", "8069"], CancellationToken.None);

        await tse.Received(1).FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await otc.Received(1).FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ── 追蹤 ──────────────────────────────────────────────────────────────────

    /// <summary>不斷把假時間往前推一個間隔，直到列舉產生下一筆。</summary>
    private async Task<StockQuote> NextAsync(IAsyncEnumerator<StockQuote> watch)
    {
        var moveNext = watch.MoveNextAsync().AsTask();
        for (var spins = 0; !moveNext.IsCompleted; spins++)
        {
            if (spins > 100)
                throw new TimeoutException("WatchAsync 沒有產生下一筆報價");
            _time.Advance(Interval);
            await Task.WhenAny(moveNext, Task.Delay(10));
        }

        Assert.True(await moveNext);
        return watch.Current;
    }

    [Fact]
    public async Task Watch_UsesTheDetectedMarket()
    {
        var otc = Source(MarketType.OTC, "6510");
        await using var watch = Service(Source(MarketType.TSE, "2330"), otc).WatchAsync("6510").GetAsyncEnumerator();

        var quote = await NextAsync(watch);

        Assert.Equal(MarketType.OTC, quote.Market);
        await otc.Received(1).FetchRealtimeQuoteAsync("6510", QuoteOptions.NoCache, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Watch_AnUnknownSymbol_EndsWithNotFound()
    {
        await using var watch = Service(Source(MarketType.TSE, "2330")).WatchAsync("9999").GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<StockQueryException>(() => NextAsync(watch));

        Assert.Equal(StockErrorCodes.NotFound, error.ErrorCode);
    }

    [Fact]
    public async Task Watch_AFailingList_IsRetriedOnTheNextInterval()
    {
        var tse = Source(MarketType.TSE, "2330");
        var attempts = 0;
        tse.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
           .Returns(_ => ++attempts == 1
               ? throw new HttpRequestException("isin down")
               : Task.FromResult<IReadOnlyDictionary<string, StockData>>(new Dictionary<string, StockData>
               {
                   ["2330"] = new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE },
               }));
        await using var watch = Service(tse).WatchAsync("2330").GetAsyncEnumerator();

        var quote = await NextAsync(watch);

        Assert.Equal("2330", quote.Symbol);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WatchMany_UsesOneBatchPerDetectedMarket()
    {
        var tse = Source(MarketType.TSE, "2330");
        var otc = Source(MarketType.OTC, "6510");
        await using var watch = Service(tse, otc).WatchAsync(["2330", "6510"]).GetAsyncEnumerator();

        var quotes = new[] { await NextAsync(watch), await NextAsync(watch) };

        Assert.Equal([("2330", MarketType.TSE), ("6510", MarketType.OTC)], quotes.Select(q => (q.Symbol, q.Market)));
        await tse.Received(1).FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), QuoteOptions.NoCache, Arg.Any<CancellationToken>());
        await otc.Received(1).FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), QuoteOptions.NoCache, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WatchMany_AnyUnknownSymbol_EndsWithNotFound()
    {
        await using var watch = Service(Source(MarketType.TSE, "2330")).WatchAsync(["2330", "9999"]).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<StockQueryException>(() => NextAsync(watch));

        Assert.Equal(StockErrorCodes.NotFound, error.ErrorCode);
        Assert.Contains("9999", error.Message);
    }

    // ── 參數檢查 ──────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidArguments_AreRejectedWhenCalled()
    {
        var tse = Source(MarketType.TSE, "2330");
        var service = Service(tse);

        Assert.Throws<ArgumentException>(() => { _ = service.GetRealtimeQuoteAsync("2330|x"); });
        Assert.Throws<ArgumentException>(() => { _ = service.GetRealtimeQuotesAsync(["2330", "2330.tw|tse_2317"]); });
        Assert.Throws<ArgumentNullException>(() => { _ = service.GetRealtimeQuotesAsync((IEnumerable<string>)null!); });
        Assert.Throws<ArgumentException>(() => { _ = service.GetHistoricalDataAsync(
            "2330", new DateOnly(2025, 9, 30), new DateOnly(2025, 9, 1)); });
        Assert.Throws<ArgumentException>(() => service.WatchAsync(" "));
        Assert.Throws<ArgumentException>(() => service.WatchAsync(Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.WatchAsync(["2330"], TimeSpan.FromSeconds(1)));
        tse.DidNotReceiveWithAnyArgs().FetchStockListAsync(default, default);
    }
}
