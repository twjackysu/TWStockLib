using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace TWStockLib.Core.Tests;

/// <summary>多檔 <c>WatchAsync</c>：每一輪一次批次查詢，只回傳有變動的那幾檔。</summary>
public class WatchManyAsyncTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();

    /// <summary>
    /// 每一輪依序回應 <paramref name="rounds"/>：代號 → 成交價的字典（不在字典中的代號查無資料），
    /// 或要拋出的例外；用完後重複最後一個。
    /// </summary>
    private static IStockDataSource Source(MarketType market, params object[] rounds)
    {
        var pending = new Queue<object>(rounds);
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(market);
        source.FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns(ci => (pending.Count > 1 ? pending.Dequeue() : pending.Peek()) switch
              {
                  Exception error => throw error,
                  Dictionary<string, decimal> prices => Task.FromResult<IReadOnlyDictionary<string, StockQuote>>(ci
                      .Arg<IReadOnlyList<string>>()
                      .Where(prices.ContainsKey)
                      .ToFrozenDictionary(s => s, s => new StockQuote { Symbol = s, Name = s, Market = market, LastPrice = prices[s] })),
                  _ => throw new InvalidOperationException(),
              });
        return source;
    }

    private static Dictionary<string, decimal> Prices(params (string Symbol, decimal Price)[] prices)
        => prices.ToDictionary(p => p.Symbol, p => p.Price);

    private StockMarketService Service(params IStockDataSource[] sources)
        => new(sources, NullLogger<StockMarketService>.Instance, _time);

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
    public async Task YieldsEveryCurrentQuote_ThenOnlyTheOnesThatChanged()
    {
        var tse = Source(MarketType.TSE,
            Prices(("2330", 100m), ("2317", 200m)),
            Prices(("2330", 100m), ("2317", 200m)),
            Prices(("2330", 100m), ("2317", 201m)));
        await using var watch = Service(tse).WatchAsync([("2330", MarketType.TSE), ("2317", MarketType.TSE)]).GetAsyncEnumerator();

        var first = await NextAsync(watch);
        var second = await NextAsync(watch);
        var changed = await NextAsync(watch);

        Assert.Equal(("2330", 100m), (first.Symbol, first.LastPrice!.Value));
        Assert.Equal(("2317", 200m), (second.Symbol, second.LastPrice!.Value));
        Assert.Equal(("2317", 201m), (changed.Symbol, changed.LastPrice!.Value));
        await tse.Received(3).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "2330", "2317" })), QuoteOptions.NoCache, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachMarketIsQueriedOncePerRound()
    {
        var tse = Source(MarketType.TSE, Prices(("2330", 100m)));
        var otc = Source(MarketType.OTC, Prices(("6510", 3000m), ("8069", 50m)));
        await using var watch = Service(tse, otc)
            .WatchAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC), ("8069", MarketType.OTC)])
            .GetAsyncEnumerator();

        var symbols = new[] { await NextAsync(watch), await NextAsync(watch), await NextAsync(watch) }.Select(q => q.Symbol);

        Assert.Equal(["2330", "6510", "8069"], symbols);
        await tse.Received(1).FetchRealtimeQuotesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>());
        await otc.Received(1).FetchRealtimeQuotesAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "6510", "8069" })), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingMarket_DoesNotStopTheOthers_AndIsRetriedNextRound()
    {
        var tse = Source(MarketType.TSE, Prices(("2330", 100m)), Prices(("2330", 101m)));
        var otc = Source(MarketType.OTC, new HttpRequestException("boom"), Prices(("6510", 3000m)));
        await using var watch = Service(tse, otc)
            .WatchAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC)])
            .GetAsyncEnumerator();

        var first = await NextAsync(watch);
        var next = new[] { await NextAsync(watch), await NextAsync(watch) };

        Assert.Equal("2330", first.Symbol);
        Assert.Equal(["2330", "6510"], next.Select(q => q.Symbol));
        Assert.Equal(101m, next[0].LastPrice);
    }

    [Fact]
    public async Task ASymbolNeverFound_EndsTheWatchWithNotFound()
    {
        var tse = Source(MarketType.TSE, Prices(("2330", 100m)));
        await using var watch = Service(tse).WatchAsync([("2330", MarketType.TSE), ("9999", MarketType.TSE)]).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<StockQueryException>(() => NextAsync(watch));

        Assert.Equal(StockErrorCodes.NotFound, error.ErrorCode);
        Assert.Contains("9999", error.Message);
    }

    [Fact]
    public async Task ASymbolMissingAfterItWasFound_IsOnlyLogged()
    {
        var tse = Source(MarketType.TSE,
            Prices(("2330", 100m), ("2317", 200m)),
            Prices(("2330", 101m)),
            Prices(("2330", 101m), ("2317", 202m)));
        await using var watch = Service(tse).WatchAsync([("2330", MarketType.TSE), ("2317", MarketType.TSE)]).GetAsyncEnumerator();

        await NextAsync(watch);
        await NextAsync(watch);
        var afterGap = new[] { await NextAsync(watch), await NextAsync(watch) };

        Assert.Equal([("2330", 101m), ("2317", 202m)], afterGap.Select(q => (q.Symbol, q.LastPrice!.Value)));
    }

    [Fact]
    public async Task Cancellation_EndsTheWatchWithOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var tse = Source(MarketType.TSE, Prices(("2330", 100m)));
        await using var watch = Service(tse).WatchAsync([("2330", MarketType.TSE)], ct: cts.Token).GetAsyncEnumerator();
        await NextAsync(watch);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch.MoveNextAsync().AsTask());
    }

    [Fact]
    public void InvalidArguments_AreRejectedWhenCalled_NotWhenEnumerated()
    {
        var service = Service(Source(MarketType.TSE, Prices()));

        Assert.Throws<ArgumentException>(() => service.WatchAsync(Array.Empty<(string, MarketType)>()));
        Assert.Throws<ArgumentNullException>(() => service.WatchAsync((IEnumerable<(string, MarketType)>)null!));
        Assert.Throws<ArgumentException>(() => service.WatchAsync([("2330", MarketType.TSE), ("6510", MarketType.OTC)]));
        Assert.Throws<ArgumentException>(() => service.WatchAsync([("2330", MarketType.TSE), ("2330", MarketType.OTC)]));
        Assert.Throws<ArgumentException>(() => service.WatchAsync([("2330|tse_2317", MarketType.TSE)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.WatchAsync([("2330", MarketType.TSE)], TimeSpan.FromSeconds(1)));
    }
}
