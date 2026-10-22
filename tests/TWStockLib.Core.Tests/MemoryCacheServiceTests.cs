using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Abstractions;

namespace TWStockLib.Core.Tests;

public class MemoryCacheServiceTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);

    private static MemoryCacheService Build() => new(new MemoryCache(new MemoryCacheOptions()));

    private sealed class Counter
    {
        public int Calls;
        public Task<string?> Next(string? value) { Calls++; return Task.FromResult(value); }
    }

    [Fact]
    public async Task GetOrSetAsync_SecondCall_IsServedFromCache()
    {
        var cache = Build();
        var counter = new Counter();

        var first = await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry);
        var second = await cache.GetOrSetAsync("k", _ => counter.Next("b"), Expiry);

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_NullIsNotCached()
    {
        var cache = Build();
        var counter = new Counter();

        await cache.GetOrSetAsync("k", _ => counter.Next(null), Expiry);
        var second = await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry);

        Assert.Equal("a", second);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_RunsTheFactoryEvenWhenCached()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry);

        var refreshed = await cache.RefreshAsync("k", _ => counter.Next("b"), Expiry);

        Assert.Equal("b", refreshed);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesTheCacheForLaterCalls()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry);
        await cache.RefreshAsync("k", _ => counter.Next("b"), Expiry);

        var later = await cache.GetOrSetAsync("k", _ => counter.Next("c"), Expiry);

        Assert.Equal("b", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_NullResult_DoesNotEvictTheCachedValue()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry);

        var refreshed = await cache.RefreshAsync("k", _ => counter.Next(null), Expiry);
        var later = await cache.GetOrSetAsync("k", _ => counter.Next("c"), Expiry);

        Assert.Null(refreshed);
        Assert.Equal("a", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_OnAnEmptyCache_BehavesLikeANormalMiss()
    {
        var cache = Build();
        var counter = new Counter();

        var first = await cache.RefreshAsync("k", _ => counter.Next("a"), Expiry);
        var second = await cache.GetOrSetAsync("k", _ => counter.Next("b"), Expiry);

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }

    // ── 合併同時發生的未命中（single-flight）──────────────────────────────────

    /// <summary>被呼叫後停在 <see cref="Release"/> 之前的 factory，記錄呼叫次數與收到的 token。</summary>
    private sealed class GatedFactory
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public CancellationToken Token { get; private set; }
        public Task Started => _started.Task;

        public async Task<string?> Run(CancellationToken token, string? value = "v")
        {
            Interlocked.Increment(ref _calls);
            Token = token;
            _started.TrySetResult();
            await _release.Task.WaitAsync(token);
            return value;
        }

        public void Release() => _release.TrySetResult();
    }

    [Fact]
    public async Task GetOrSetAsync_ConcurrentMisses_RunTheFactoryOnce()
    {
        var cache = Build();
        var factory = new GatedFactory();

        var callers = Enumerable.Range(0, 10)
            .Select(_ => cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, CancellationToken.None))
            .ToArray();
        await factory.Started;
        factory.Release();
        var results = await Task.WhenAll(callers);

        Assert.All(results, r => Assert.Equal("v", r));
        Assert.Equal(1, factory.Calls);
        Assert.Equal("v", await cache.GetOrSetAsync("k", t => factory.Run(t, "other"), Expiry, CancellationToken.None));
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_OneCallerCancelling_LeavesTheSharedLoadRunningForTheOthers()
    {
        var cache = Build();
        var factory = new GatedFactory();
        using var leaving = new CancellationTokenSource();

        var first = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, leaving.Token);
        await factory.Started;
        var second = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, CancellationToken.None);
        leaving.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(factory.Token.IsCancellationRequested);
        factory.Release();
        Assert.Equal("v", await second);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_EveryCallerCancelling_CancelsTheFactory_AndTheNextCallStartsOver()
    {
        var cache = Build();
        var factory = new GatedFactory();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();

        var first = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, a.Token);
        await factory.Started;
        var second = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, b.Token);
        a.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(factory.Token.IsCancellationRequested);
        b.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        Assert.True(factory.Token.IsCancellationRequested);

        var counter = new Counter();
        Assert.Equal("fresh", await cache.GetOrSetAsync("k", _ => counter.Next("fresh"), Expiry, CancellationToken.None));
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_AnAlreadyCancelledCaller_DoesNotStartALoad()
    {
        var cache = Build();
        var counter = new Counter();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry, new CancellationToken(canceled: true)));

        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_AFailedSharedLoad_FailsEveryWaiter_AndIsNotCached()
    {
        var cache = Build();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<string?> Failing(CancellationToken t)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            throw new HttpRequestException("upstream down");
        }

        var callers = Enumerable.Range(0, 3)
            .Select(_ => cache.GetOrSetAsync("k", Failing, Expiry, CancellationToken.None))
            .ToArray();
        release.SetResult();

        foreach (var caller in callers)
            await Assert.ThrowsAsync<HttpRequestException>(() => caller);
        Assert.Equal(1, calls);

        var counter = new Counter();
        Assert.Equal("a", await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrSetAsync_ASharedNullResult_IsNotCached()
    {
        var cache = Build();
        var factory = new GatedFactory();

        var callers = Enumerable.Range(0, 3)
            .Select(_ => cache.GetOrSetAsync("k", t => factory.Run(t, value: null), Expiry, CancellationToken.None))
            .ToArray();
        await factory.Started;
        factory.Release();

        Assert.All(await Task.WhenAll(callers), Assert.Null);
        Assert.Equal(1, factory.Calls);
        var counter = new Counter();
        Assert.Equal("a", await cache.GetOrSetAsync("k", _ => counter.Next("a"), Expiry, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentKeys_AreNotMerged()
    {
        var cache = Build();
        var counter = new Counter();

        await Task.WhenAll(
            cache.GetOrSetAsync("a", _ => counter.Next("a"), Expiry, CancellationToken.None),
            cache.GetOrSetAsync("b", _ => counter.Next("b"), Expiry, CancellationToken.None));

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_DuringASharedLoad_RunsItsOwnFactory_AndNullStillDoesNotOverwrite()
    {
        var cache = Build();
        var factory = new GatedFactory();
        var counter = new Counter();

        var shared = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry, CancellationToken.None);
        await factory.Started;
        var refreshed = await cache.RefreshAsync("k", _ => counter.Next(null), Expiry);
        factory.Release();

        Assert.Null(refreshed);
        Assert.Equal(1, counter.Calls);
        Assert.Equal("v", await shared);
        Assert.Equal("v", await cache.GetOrSetAsync("k", _ => counter.Next("x"), Expiry));
    }

    // ── TryGetValue：只查快取 ──────────────────────────────────────────────────

    [Fact]
    public async Task TryGetValue_FindsOnlyCachedNonNullValues()
    {
        var cache = Build();
        await cache.GetOrSetAsync("k", _ => Task.FromResult<string?>("a"), Expiry);

        Assert.True(cache.TryGetValue<string>("k", out var value));
        Assert.Equal("a", value);
        Assert.False(cache.TryGetValue<string>("missing", out _));
    }

    [Fact]
    public async Task TryGetValue_DoesNotJoinALoadInProgress()
    {
        // 批次報價先以 TryGetValue 挑出未快取的代號；它不能等候或發起載入，否則會拖住整批查詢
        var cache = Build();
        var factory = new GatedFactory();

        var loading = cache.GetOrSetAsync("k", t => factory.Run(t), Expiry);
        await factory.Started;

        Assert.False(cache.TryGetValue<string>("k", out _));

        factory.Release();
        Assert.Equal("v", await loading);
        Assert.True(cache.TryGetValue<string>("k", out var value));
        Assert.Equal("v", value);
    }
}
