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
        Assert.Equivalent(quote, result.Value);
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
    // ── 略過快取（bypassCache）──────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_BypassCache_AsksTheSourceToBypass()
    {
        var tse = FakeSource(MarketType.TSE);
        var quote = new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m };
        tse.FetchRealtimeQuoteAsync("2330", true, Arg.Any<CancellationToken>()).Returns(quote);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true);

        Assert.True(result.IsSuccess);
        Assert.Equivalent(quote, result.Value);
        await tse.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_NoBypass_KeepsUsingTheOriginalSourceMethod()
    {
        // 既有的 IStockDataSource 實作／mock 只認得舊方法：bypassCache=false 必須走舊路徑，行為與升級前一致。
        var tse = FakeSource(MarketType.TSE);
        var quote = new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m };
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<CancellationToken>()).Returns(quote);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: false);

        Assert.Equivalent(quote, result.Value);
        await tse.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_BypassCache_UnknownMarket_ReturnsSourceNotFound()
    {
        var result = await BuildService(FakeSource(MarketType.TSE))
            .GetRealtimeQuoteAsync("2330", MarketType.OTC, bypassCache: true);

        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_BypassCache_SourceReturnsNull_ReturnsNotFound()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), true, Arg.Any<CancellationToken>()).Returns((StockQuote?)null);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("9999", MarketType.TSE, bypassCache: true);

        Assert.Equal(StockErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_BypassCache_SourceThrows_ReturnsUpstreamError()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), true, Arg.Any<CancellationToken>())
           .Returns<StockQuote?>(_ => throw new HttpRequestException("boom"));

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true);

        Assert.Equal(StockErrorCodes.UpstreamError, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_BypassCache_StillNotifiesPriceObservers()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync("2330", true, Arg.Any<CancellationToken>())
           .Returns(new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m },
                    new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1010m });
        var service = BuildService(tse);
        var observer = Substitute.For<IStockPriceObserver>();
        service.SubscribePriceChanges("2330", observer);

        await service.GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true);
        await service.GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true);

        observer.Received(1).OnPriceChanged("2330", 1010m, 1000m);
    }

    // ── 觀察者的例外不影響查詢結果 ─────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_AnObserverThrows_StillReturnsTheQuote_AndNotifiesTheOthers()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<CancellationToken>())
           .Returns(new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m },
                    new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1010m });
        var service = BuildService(tse);
        var faulty = Substitute.For<IStockPriceObserver>();
        faulty.When(o => o.OnPriceChanged(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<decimal>()))
              .Do(_ => throw new InvalidOperationException("observer bug"));
        var healthy = Substitute.For<IStockPriceObserver>();
        service.SubscribePriceChanges("2330", faulty);
        service.SubscribePriceChanges("2330", healthy);

        await service.GetRealtimeQuoteAsync("2330", MarketType.TSE);
        var result = await service.GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        Assert.Equal(1010m, result.Value!.LastPrice);
        healthy.Received(1).OnPriceChanged("2330", 1010m, 1000m);
    }

    // ── 取消與逾時 ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, Func<IStockMarketService, CancellationToken, Task<string?>>> Queries = new()
    {
        ["quote"] = async (s, ct) => (await s.GetRealtimeQuoteAsync("2330", MarketType.TSE, ct)).ErrorCode,
        ["quote-bypass"] = async (s, ct) => (await s.GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true, ct)).ErrorCode,
        ["history"] = async (s, ct) => (await s.GetHistoricalDataAsync(
            "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), ct)).ErrorCode,
        ["list"] = async (s, ct) => (await s.GetStockListAsync(MarketType.TSE, ct: ct)).ErrorCode,
        ["all"] = async (s, ct) => (await s.GetAllStockListAsync(ct: ct)).ErrorCode,
    };

    /// <summary>所有查詢都以 <paramref name="error"/>（可取得呼叫端傳入的 token）失敗的資料來源。</summary>
    private static IStockDataSource FailingSource(MarketType market, Func<CancellationToken, Exception> error)
    {
        var source = FakeSource(market);
        source.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns<StockQuote?>(ci => throw error(ci.Arg<CancellationToken>()));
        source.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns<StockQuote?>(ci => throw error(ci.Arg<CancellationToken>()));
        source.FetchHistoricalDataAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
              .Returns<IReadOnlyList<StockHistory>>(ci => throw error(ci.Arg<CancellationToken>()));
        source.FetchStockListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
              .Returns<IReadOnlyDictionary<string, StockData>>(ci => throw error(ci.Arg<CancellationToken>()));
        return source;
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("quote-bypass")]
    [InlineData("history")]
    [InlineData("list")]
    [InlineData("all")]
    public async Task CallerCancellation_IsThrown_NotTurnedIntoAFailedResult(string query)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = BuildService(FailingSource(MarketType.TSE, ct => new OperationCanceledException(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Queries[query](service, cts.Token));
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("quote-bypass")]
    [InlineData("history")]
    [InlineData("list")]
    [InlineData("all")]
    public async Task AnHttpClientTimeout_BecomesATimeoutFailure(string query)
    {
        // HttpClient 逾時丟的是 TaskCanceledException，但呼叫端的 token 並沒有被取消
        var service = BuildService(FailingSource(MarketType.TSE, _ => new TaskCanceledException("HttpClient.Timeout elapsed")));

        var errorCode = await Queries[query](service, CancellationToken.None);

        Assert.Equal(StockErrorCodes.Timeout, errorCode);
    }

    // ── 未指定市場 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_DefaultMarket_ReturnsSourceNotFound()
    {
        var result = await BuildService(FakeSource(MarketType.TSE), FakeSource(MarketType.OTC))
            .GetRealtimeQuoteAsync("2330", default);

        Assert.Equal(MarketType.Unknown, default(MarketType));
        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }

    // ── 同一市場重複註冊 ──────────────────────────────────────────────────────

    [Fact]
    public async Task TwoSourcesForTheSameMarket_TheLastRegisteredWins()
    {
        var replaced = FakeSource(MarketType.TSE);
        var replacement = FakeSource(MarketType.TSE);
        replacement.FetchRealtimeQuoteAsync("2330", Arg.Any<CancellationToken>())
                   .Returns(new StockQuote { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = 1000m });

        var result = await BuildService(replaced, replacement).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        await replaced.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
