using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace TWStockLib.Twse.Http;

/// <summary>
/// 依請求的主機名稱分開計算的限流器：www.twse.com.tw、mis.twse.com.tw、www.tpex.org.tw 等各自擁有一個
/// <see cref="SlidingLogRateLimiter"/>，查歷史資料不會拖慢即時報價。
/// 內建來源只連少數幾個固定主機，因此不需要清掉閒置的分區。
/// </summary>
internal sealed class PerHostRateLimiter(int permitLimit, TimeSpan window, TimeProvider timeProvider)
    : PartitionedRateLimiter<HttpRequestMessage>
{
    private readonly ConcurrentDictionary<string, SlidingLogRateLimiter> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public override RateLimiterStatistics? GetStatistics(HttpRequestMessage resource) => For(resource).GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(HttpRequestMessage resource, int permitCount)
        => For(resource).AttemptAcquire(permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpRequestMessage resource, int permitCount, CancellationToken cancellationToken)
        => For(resource).AcquireAsync(permitCount, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeHosts();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        DisposeHosts();
        return ValueTask.CompletedTask;
    }

    private SlidingLogRateLimiter For(HttpRequestMessage request) =>
        _hosts.GetOrAdd(request.RequestUri?.Host ?? string.Empty,
            _ => new SlidingLogRateLimiter(permitLimit, window, timeProvider));

    private void DisposeHosts()
    {
        foreach (var limiter in _hosts.Values)
            limiter.Dispose();
    }
}
