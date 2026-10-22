using System.Threading.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using TWStockLib.Twse.Http;
using static TWStockLib.Twse.Tests.FakeTimeProviderExtensions;

namespace TWStockLib.Twse.Tests;

public class SlidingLogRateLimiterTests
{
    private readonly FakeTimeProvider _time = new();

    private SlidingLogRateLimiter Create(int permitLimit = 3) => new(permitLimit, TimeSpan.FromSeconds(5), _time);

    [Fact]
    public async Task AcquireAsync_TheFourthPermitInAWindow_WaitsUntilTheFirstSlidesOut()
    {
        using var limiter = Create();
        for (var i = 0; i < 3; i++)
            Assert.True((await limiter.AcquireAsync()).IsAcquired);

        var fourth = limiter.AcquireAsync().AsTask();
        Assert.False(fourth.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        Assert.False(fourth.IsCompleted);

        _time.Advance(TimeSpan.FromTicks(1));
        Assert.True((await fourth.WaitAsync(RealTimeout)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_PermitsSlideOutOneByOne_AndWaitersAreServedInOrder()
    {
        // 與分段補充的限流不同：t=0、1、2 各取一個許可，t=5 只會空出 t=0 那一個
        using var limiter = Create();
        await limiter.AcquireAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        await limiter.AcquireAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        await limiter.AcquireAsync();

        var first = limiter.AcquireAsync().AsTask();
        var second = limiter.AcquireAsync().AsTask();

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.True((await first.WaitAsync(RealTimeout)).IsAcquired);
        Assert.False(second.IsCompleted);

        // second 在 first 之後才輪到，等候的計時器在背景建立，因此只斷言下限：最快 t=6（t=1 的許可滑出）
        var waited = await _time.AdvanceUntilCompletedAsync(second, TimeSpan.FromMilliseconds(100));
        Assert.True((await second).IsAcquired);
        Assert.True(waited >= TimeSpan.FromSeconds(1), $"只多等了 {waited}");
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileWaiting_ThrowsAndLetsTheNextOneIn()
    {
        using var limiter = Create();
        for (var i = 0; i < 3; i++)
            await limiter.AcquireAsync();

        using var cts = new CancellationTokenSource();
        var cancelled = limiter.AcquireAsync(1, cts.Token).AsTask();
        var next = limiter.AcquireAsync().AsTask();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentQueuedCount);
        Assert.False(next.IsCompleted);

        var waited = await _time.AdvanceUntilCompletedAsync(next, TimeSpan.FromMilliseconds(100));
        Assert.True((await next).IsAcquired);
        Assert.True(waited >= TimeSpan.FromSeconds(5), $"只等了 {waited}");
        Assert.Equal(2, limiter.GetStatistics()!.CurrentAvailablePermits); // 被取消的請求沒有用掉許可
    }

    [Fact]
    public void AttemptAcquire_WhenExhausted_FailsWithTheTimeUntilAPermitFrees()
    {
        using var limiter = Create();
        for (var i = 0; i < 3; i++)
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        _time.Advance(TimeSpan.FromSeconds(2));

        using var lease = limiter.AttemptAcquire();

        Assert.False(lease.IsAcquired);
        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(3), retryAfter);
    }

    [Fact]
    public async Task Dispose_FailsTheWaitingRequests()
    {
        var limiter = Create(permitLimit: 1);
        await limiter.AcquireAsync();
        var waiting = limiter.AcquireAsync().AsTask();

        limiter.Dispose();

        Assert.False((await waiting.WaitAsync(RealTimeout)).IsAcquired);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => limiter.AcquireAsync().AsTask());
    }
}
