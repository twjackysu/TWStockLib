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

    private static StockQuote Quote(decimal lastPrice)
        => new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE, LastPrice = lastPrice };

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
        var quote = Quote(1000m);
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>()).Returns(quote);

        var result = await BuildService(tse, otc).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        Assert.Same(quote, result.Value);
        await otc.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>());
    }

    // ── 成功 / 失敗收斂 ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_SourceReturnsNull_ReturnsNotFound()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
           .Returns((StockQuote?)null);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("9999", MarketType.TSE);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_SourceThrows_ReturnsUpstreamError_WithTheCause()
    {
        var tse = FakeSource(MarketType.TSE);
        var cause = new HttpRequestException("boom");
        tse.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
           .Returns<StockQuote?>(_ => throw cause);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.False(result.IsSuccess);
        Assert.Equal(StockErrorCodes.UpstreamError, result.ErrorCode);
        Assert.Contains("boom", result.ErrorMessage);
        Assert.Same(cause, result.Exception);
    }

    [Fact]
    public async Task GetHistoricalDataAsync_UnreadableUpstreamResponse_ReturnsParseError()
    {
        // 例如被上游擋下時回傳的是 HTML 錯誤頁，不是 JSON
        var tse = FakeSource(MarketType.TSE);
        tse.FetchHistoricalDataAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
           .Returns<IReadOnlyList<StockHistory>>(_ => throw new System.Text.Json.JsonException("'<' is an invalid start of a value."));

        var result = await BuildService(tse).GetHistoricalDataAsync(
            "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

        Assert.Equal(StockErrorCodes.ParseError, result.ErrorCode);
    }

    [Fact]
    public async Task GetAllStockListAsync_AMarketFails_ReturnsThatFailure()
    {
        var tse = FakeSource(MarketType.TSE);
        var otc = FakeSource(MarketType.OTC);
        tse.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
           .Returns(new Dictionary<string, StockData>());
        otc.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
           .Returns<IReadOnlyDictionary<string, StockData>>(_ => throw new HttpRequestException("boom"));

        var result = await BuildService(tse, otc).GetAllStockListAsync();

        Assert.Equal(StockErrorCodes.UpstreamError, result.ErrorCode);
    }

    // ── 參數檢查 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BlankSymbols_AreRejected(string? symbol)
    {
        var service = BuildService(FakeSource(MarketType.TSE));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetRealtimeQuoteAsync(symbol!, MarketType.TSE));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetHistoricalDataAsync(
            symbol!, MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30)));
    }

    [Theory]
    [InlineData("2330.tw|tse_2317")]   // 會被 MIS 當成多查一檔
    [InlineData("2330&delay=1")]
    [InlineData("2330 ")]
    [InlineData("２３３０")]           // 全形數字
    [InlineData("12345678901")]       // 超過 10 碼
    public void MalformedSymbols_AreRejectedBeforeAnyQuery(string symbol)
    {
        var tse = FakeSource(MarketType.TSE);
        var service = BuildService(tse);

        Assert.Throws<ArgumentException>(() => { _ = service.GetRealtimeQuoteAsync(symbol, MarketType.TSE); });
        Assert.Throws<ArgumentException>(() => { _ = service.GetHistoricalDataAsync(
            symbol, MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30)); });
        Assert.Throws<ArgumentException>(() => service.WatchAsync(symbol, MarketType.TSE));
        Assert.Empty(tse.ReceivedCalls().Where(c => c.GetMethodInfo().Name != "get_Market"));
    }

    [Theory]
    [InlineData("2330")]
    [InlineData("00631L")]   // ETF
    [InlineData("t00")]      // 加權指數
    [InlineData("030001")]   // 權證
    public async Task WellFormedSymbols_AreQueried(string symbol)
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync(symbol, Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>()).Returns(Quote(1m));

        var result = await BuildService(tse).GetRealtimeQuoteAsync(symbol, MarketType.TSE);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GetHistoricalDataAsync_FromAfterTo_IsRejectedBeforeAnyQuery()
    {
        var tse = FakeSource(MarketType.TSE);

        // 呼叫當下就拋出，不必等到 await
        Assert.Throws<ArgumentException>(() =>
        {
            _ = BuildService(tse).GetHistoricalDataAsync(
                "2330", MarketType.TSE, new DateOnly(2025, 9, 30), new DateOnly(2025, 9, 1));
        });
        tse.DidNotReceiveWithAnyArgs().FetchHistoricalDataAsync(default!, default, default, default);
    }

    // ── GetAllStockList 合併 ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllStockListAsync_MergesAllSources()
    {
        var tse = FakeSource(MarketType.TSE);
        var otc = FakeSource(MarketType.OTC);
        tse.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
           .Returns(new Dictionary<string, StockData>
           {
               ["2330"] = new() { Symbol = "2330", Name = "台積電", Market = MarketType.TSE },
           });
        otc.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
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

    // ── 報價選項 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRealtimeQuoteAsync_WithoutOptions_UsesTheDefaults()
    {
        var tse = FakeSource(MarketType.TSE);
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>()).Returns(Quote(1000m));

        await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        await tse.Received(1).FetchRealtimeQuoteAsync("2330", QuoteOptions.Default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_NoCache_IsPassedToTheSource()
    {
        var tse = FakeSource(MarketType.TSE);
        var quote = Quote(1000m);
        tse.FetchRealtimeQuoteAsync("2330", Arg.Is<QuoteOptions>(o => o.BypassCache), Arg.Any<CancellationToken>()).Returns(quote);

        var result = await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE, QuoteOptions.NoCache);

        Assert.Same(quote, result.Value);
    }

    [Fact]
    public async Task GetRealtimeQuoteAsync_TheCancellationTokenOnlyOverload_UsesTheDefaults()
    {
        var tse = FakeSource(MarketType.TSE);
        using var cts = new CancellationTokenSource();
        tse.FetchRealtimeQuoteAsync("2330", Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>()).Returns(Quote(1000m));

        await BuildService(tse).GetRealtimeQuoteAsync("2330", MarketType.TSE, cts.Token);

        await tse.Received(1).FetchRealtimeQuoteAsync("2330", QuoteOptions.Default, cts.Token);
    }

    // ── 取消與逾時 ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, Func<IStockMarketService, CancellationToken, Task<string?>>> Queries = new()
    {
        ["quote"] = async (s, ct) => (await s.GetRealtimeQuoteAsync("2330", MarketType.TSE, ct)).ErrorCode,
        ["quote-nocache"] = async (s, ct) => (await s.GetRealtimeQuoteAsync("2330", MarketType.TSE, QuoteOptions.NoCache, ct)).ErrorCode,
        ["history"] = async (s, ct) => (await s.GetHistoricalDataAsync(
            "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), ct)).ErrorCode,
        ["list"] = async (s, ct) => (await s.GetStockListAsync(MarketType.TSE, ct: ct)).ErrorCode,
        ["all"] = async (s, ct) => (await s.GetAllStockListAsync(ct: ct)).ErrorCode,
    };

    /// <summary>所有查詢都以 <paramref name="error"/>（可取得呼叫端傳入的 token）失敗的資料來源。</summary>
    private static IStockDataSource FailingSource(MarketType market, Func<CancellationToken, Exception> error)
    {
        var source = FakeSource(market);
        source.FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
              .Returns<StockQuote?>(ci => throw error(ci.Arg<CancellationToken>()));
        source.FetchHistoricalDataAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
              .Returns<IReadOnlyList<StockHistory>>(ci => throw error(ci.Arg<CancellationToken>()));
        source.FetchStockListAsync(Arg.Any<StockListOptions>(), Arg.Any<CancellationToken>())
              .Returns<IReadOnlyDictionary<string, StockData>>(ci => throw error(ci.Arg<CancellationToken>()));
        return source;
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("quote-nocache")]
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
    [InlineData("quote-nocache")]
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
            .GetRealtimeQuoteAsync("2330", default(MarketType));

        Assert.Equal(MarketType.Unknown, default(MarketType));
        Assert.Equal(StockErrorCodes.SourceNotFound, result.ErrorCode);
    }

    // ── 同一市場重複註冊 ──────────────────────────────────────────────────────

    [Fact]
    public async Task TwoSourcesForTheSameMarket_TheLastRegisteredWins()
    {
        var replaced = FakeSource(MarketType.TSE);
        var replacement = FakeSource(MarketType.TSE);
        replacement.FetchRealtimeQuoteAsync("2330", Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>())
                   .Returns(Quote(1000m));

        var result = await BuildService(replaced, replacement).GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        await replaced.DidNotReceive().FetchRealtimeQuoteAsync(Arg.Any<string>(), Arg.Any<QuoteOptions>(), Arg.Any<CancellationToken>());
    }
}
