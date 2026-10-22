using Microsoft.Extensions.Time.Testing;
using TWStockLib.Twse.Http;
using static TWStockLib.Twse.Tests.FakeHttpMessageHandler;
using static TWStockLib.Twse.Tests.FakeTimeProviderExtensions;

namespace TWStockLib.Twse.Tests;

public class RateLimitingHandlerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeHttpMessageHandler _network = new();

    private HttpClient CreateClient(PerHostRateLimiter limiter) => new(new RateLimitingHandler(limiter) { InnerHandler = _network });

    [Fact]
    public async Task TheFourthRequestToAHostWithinFiveSeconds_IsSentOnlyAfterTheFirstSlidesOut()
    {
        using var limiter = new PerHostRateLimiter(3, TimeSpan.FromSeconds(5), _time);
        using var client = CreateClient(limiter);
        for (var i = 0; i < 3; i++)
            await client.GetAsync(TwseHistoryUrl);

        var fourth = client.GetAsync(TwseHistoryUrl);

        Assert.False(fourth.IsCompleted);
        Assert.Equal(3, _network.Requests.Count);

        _time.Advance(TimeSpan.FromSeconds(5));
        await fourth.WaitAsync(RealTimeout);
        Assert.Equal(4, _network.Requests.Count);
    }

    [Fact]
    public async Task EachHost_HasItsOwnBudget()
    {
        using var limiter = new PerHostRateLimiter(3, TimeSpan.FromSeconds(5), _time);
        using var client = CreateClient(limiter);
        for (var i = 0; i < 3; i++)
            await client.GetAsync(TwseHistoryUrl);

        // www.twse.com.tw 的額度用完，不影響 mis.twse.com.tw 的即時報價
        await client.GetAsync(MisQuoteUrl).WaitAsync(RealTimeout);

        Assert.Equal(4, _network.Requests.Count);
    }

    [Fact]
    public async Task CancellingAWaitingRequest_NeverSendsIt()
    {
        using var limiter = new PerHostRateLimiter(3, TimeSpan.FromSeconds(5), _time);
        using var client = CreateClient(limiter);
        for (var i = 0; i < 3; i++)
            await client.GetAsync(TwseHistoryUrl);

        using var cts = new CancellationTokenSource();
        var fourth = client.GetAsync(TwseHistoryUrl, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fourth);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, _network.Requests.Count);
    }
}
