using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace TWStockLib.Core.Tests;

public class WatchAsyncTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();

    private static StockQuote Quote(decimal? lastPrice)
        => new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = lastPrice };

    /// <summary>依序回應 <paramref name="responses"/>（報價、<c>null</c> 代表查無資料、或要拋出的例外），用完後重複最後一個。</summary>
    private static IStockDataSource Source(params object?[] responses)
    {
        var pending = new Queue<object?>(responses);
        var source = Substitute.For<IStockDataSource>();
        source.Market.Returns(MarketType.TSE);
        source.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns(_ => (pending.Count > 1 ? pending.Dequeue() : pending.Peek()) switch
              {
                  Exception error => throw error,
                  StockQuote quote => Task.FromResult<StockQuote?>(quote),
                  _ => Task.FromResult<StockQuote?>(null),
              });
        return source;
    }

    private StockMarketService Service(IStockDataSource source)
        => new([source], NullLogger<StockMarketService>.Instance, _time);

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
    public async Task YieldsTheCurrentQuote_ThenOnlyPriceChanges()
    {
        var source = Source(Quote(100m), Quote(100m), Quote(100m), Quote(101m));
        await using var watch = Service(source).WatchAsync("2330", MarketType.TSE).GetAsyncEnumerator();

        var first = await NextAsync(watch);
        var second = await NextAsync(watch);

        Assert.Equal(100m, first.LastPrice);
        Assert.Equal(101m, second.LastPrice);
        await source.Received(4).FetchRealtimeQuoteAsync("2330", QuoteOptions.NoCache, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATransientFailure_IsRetriedOnTheNextInterval()
    {
        var source = Source(Quote(100m), new HttpRequestException("boom"), Quote(101m));
        await using var watch = Service(source).WatchAsync("2330", MarketType.TSE).GetAsyncEnumerator();

        await NextAsync(watch);
        var afterFailure = await NextAsync(watch);

        Assert.Equal(101m, afterFailure.LastPrice);
    }

    [Fact]
    public async Task AFirstTradeAfterTheOpen_IsAChange()
    {
        // 盤前沒有成交價（null），開盤第一筆成交要回傳
        var source = Source(Quote(null), Quote(100m));
        await using var watch = Service(source).WatchAsync("2330", MarketType.TSE).GetAsyncEnumerator();

        Assert.Null((await NextAsync(watch)).LastPrice);
        Assert.Equal(100m, (await NextAsync(watch)).LastPrice);
    }

    [Fact]
    public async Task AnUnknownSymbol_EndsTheWatchWithNotFound()
    {
        var source = Source([null]);
        await using var watch = Service(source).WatchAsync("9999", MarketType.TSE).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<StockQueryException>(() => NextAsync(watch));

        Assert.Equal(StockErrorCodes.NotFound, error.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_EndsTheWatchWithOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var source = Source(Quote(100m));
        await using var watch = Service(source).WatchAsync("2330", MarketType.TSE, ct: cts.Token).GetAsyncEnumerator();
        await NextAsync(watch);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch.MoveNextAsync().AsTask());
    }

    [Fact]
    public void InvalidArguments_AreRejectedWhenCalled_NotWhenEnumerated()
    {
        var service = Service(Source(Quote(100m)));

        Assert.Throws<ArgumentOutOfRangeException>(() => service.WatchAsync("2330", MarketType.TSE, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => service.WatchAsync("2330", MarketType.OTC));
        Assert.Throws<ArgumentException>(() => service.WatchAsync(" ", MarketType.TSE));
    }
}
