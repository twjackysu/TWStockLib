using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Cache;

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

        var first = await cache.GetOrSetAsync("k", () => counter.Next("a"), Expiry);
        var second = await cache.GetOrSetAsync("k", () => counter.Next("b"), Expiry);

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_NullIsNotCached()
    {
        var cache = Build();
        var counter = new Counter();

        await cache.GetOrSetAsync("k", () => counter.Next(null), Expiry);
        var second = await cache.GetOrSetAsync("k", () => counter.Next("a"), Expiry);

        Assert.Equal("a", second);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_RunsTheFactoryEvenWhenCached()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), Expiry);

        var refreshed = await cache.RefreshAsync("k", () => counter.Next("b"), Expiry);

        Assert.Equal("b", refreshed);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesTheCacheForLaterCalls()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), Expiry);
        await cache.RefreshAsync("k", () => counter.Next("b"), Expiry);

        var later = await cache.GetOrSetAsync("k", () => counter.Next("c"), Expiry);

        Assert.Equal("b", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_NullResult_DoesNotEvictTheCachedValue()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), Expiry);

        var refreshed = await cache.RefreshAsync("k", () => counter.Next(null), Expiry);
        var later = await cache.GetOrSetAsync("k", () => counter.Next("c"), Expiry);

        Assert.Null(refreshed);
        Assert.Equal("a", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task RefreshAsync_OnAnEmptyCache_BehavesLikeANormalMiss()
    {
        var cache = Build();
        var counter = new Counter();

        var first = await cache.RefreshAsync("k", () => counter.Next("a"), Expiry);
        var second = await cache.GetOrSetAsync("k", () => counter.Next("b"), Expiry);

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }
}
