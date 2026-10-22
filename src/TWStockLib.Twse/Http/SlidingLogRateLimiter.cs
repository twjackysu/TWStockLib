using System.Threading.RateLimiting;

namespace TWStockLib.Twse.Http;

/// <summary>
/// 嚴格的滑動視窗限流：任何長度為 window 的區間內最多核發 permitLimit 個許可，
/// 拿不到的請求依先來後到排隊，等最舊的許可滑出視窗再放行。許可以時間計算，租約釋放時不會歸還。
/// </summary>
/// <remarks>
/// 不用內建的 <see cref="SlidingWindowRateLimiter"/> / <see cref="TokenBucketRateLimiter"/>：它們以計時器分段補充，
/// 分段邊界前後的短時間內可放行接近兩倍的請求；且直接讀取 <see cref="System.Diagnostics.Stopwatch"/>，
/// 無法以 <see cref="TimeProvider"/> 測試。這裡記下每個許可的核發時間，直接以此判斷。
/// </remarks>
internal sealed class SlidingLogRateLimiter : RateLimiter
{
    private static readonly RateLimitLease AcquiredLease = new Lease(isAcquired: true, retryAfter: null);

    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    // 視窗內各許可的核發時間（TimeProvider 時間戳），由舊到新
    private readonly Queue<long> _grantedAt = new();

    // 排隊的請求一次只放一個去等許可；SemaphoreSlim 的非同步等待是先進先出
    private readonly SemaphoreSlim _line = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();

    private readonly long _createdAt;
    private long? _lastGrantedAt;
    private int _queuedCount;
    private long _successfulLeases;
    private long _failedLeases;

    public SlidingLogRateLimiter(int permitLimit, TimeSpan window, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        _permitLimit = permitLimit;
        _window = window;
        _timeProvider = timeProvider;
        _createdAt = timeProvider.GetTimestamp();
    }

    /// <summary>所有許可都可用、也沒有人排隊的時間長度。</summary>
    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_lock)
            {
                var now = _timeProvider.GetTimestamp();
                Prune(now);
                if (_grantedAt.Count > 0 || _queuedCount > 0)
                    return null;

                return _lastGrantedAt is { } last
                    ? _timeProvider.GetElapsedTime(last, now) - _window
                    : _timeProvider.GetElapsedTime(_createdAt, now);
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_lock)
        {
            Prune(_timeProvider.GetTimestamp());
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = _permitLimit - _grantedAt.Count,
                CurrentQueuedCount = _queuedCount,
                TotalSuccessfulLeases = _successfulLeases,
                TotalFailedLeases = _failedLeases,
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, _permitLimit);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);

            // 有人在排隊時不插隊，也無法預估要等多久
            TimeSpan? retryAfter = null;
            if (_queuedCount == 0)
            {
                if (TryGrant(permitCount, out var wait))
                    return AcquiredLease;
                retryAfter = wait;
            }

            _failedLeases++;
            return new Lease(isAcquired: false, retryAfter);
        }
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, _permitLimit);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);

            if (_queuedCount == 0 && TryGrant(permitCount, out _))
                return AcquiredLease;

            _queuedCount++;
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
        var inLine = false;
        try
        {
            await _line.WaitAsync(waiting.Token);
            inLine = true;

            while (true)
            {
                TimeSpan retryAfter;
                lock (_lock)
                {
                    if (TryGrant(permitCount, out retryAfter))
                        return AcquiredLease;
                }

                await Task.Delay(retryAfter, _timeProvider, waiting.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 限流器被釋放：還在等的請求一律失敗，而不是無限期卡住
            lock (_lock)
                _failedLeases++;
            return new Lease(isAcquired: false, retryAfter: null);
        }
        finally
        {
            if (inLine)
                _line.Release();
            lock (_lock)
                _queuedCount--;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _disposed.Cancel();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _disposed.Cancel();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 視窗內還有額度就記下核發時間並回傳 <c>true</c>；否則回傳 <c>false</c> 與需要等候的時間。
    /// <paramref name="permitCount"/> 為 0 時只確認是否還有額度。呼叫端需持有 <see cref="_lock"/>。
    /// </summary>
    private bool TryGrant(int permitCount, out TimeSpan retryAfter)
    {
        var now = _timeProvider.GetTimestamp();
        Prune(now);

        var needed = Math.Max(permitCount, 1);
        var available = _permitLimit - _grantedAt.Count;
        if (available >= needed)
        {
            for (var i = 0; i < permitCount; i++)
                _grantedAt.Enqueue(now);
            if (permitCount > 0)
            {
                _lastGrantedAt = now;
                _successfulLeases++;
            }
            retryAfter = TimeSpan.Zero;
            return true;
        }

        // 要等到第 (needed - available) 舊的許可滑出視窗
        var expiresNext = _grantedAt.ElementAt(needed - available - 1);
        retryAfter = _window - _timeProvider.GetElapsedTime(expiresNext, now);
        return false;
    }

    private void Prune(long now)
    {
        while (_grantedAt.Count > 0 && _timeProvider.GetElapsedTime(_grantedAt.Peek(), now) >= _window)
            _grantedAt.Dequeue();
    }

    private sealed class Lease(bool isAcquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
