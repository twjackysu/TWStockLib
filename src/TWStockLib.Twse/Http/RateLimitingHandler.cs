using System.Threading.RateLimiting;

namespace TWStockLib.Twse.Http;

/// <summary>
/// 送出請求前先向限流器取得許可，額度用完時排隊等候（可由 <see cref="CancellationToken"/> 取消）。
/// 掛在具名 HttpClient 管線的最內層，使用者另外加上的重試 handler 每一次實際送出都會經過這裡。
/// </summary>
internal sealed class RateLimitingHandler(PartitionedRateLimiter<HttpRequestMessage> limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await limiter.AcquireAsync(request, permitCount: 1, cancellationToken);
        if (!lease.IsAcquired)
            throw new HttpRequestException($"無法取得送往 {request.RequestUri?.Host} 的限流許可：限流器已釋放。");

        return await base.SendAsync(request, cancellationToken);
    }
}
