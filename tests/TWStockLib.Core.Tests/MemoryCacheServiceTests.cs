using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Cache;

namespace TWStockLib.Core.Tests;

public class MemoryCacheServiceTests
{
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

        var first = await cache.GetOrSetAsync("k", () => counter.Next("a"), TimeSpan.FromMinutes(1));
        var second = await cache.GetOrSetAsync("k", () => counter.Next("b"), TimeSpan.FromMinutes(1));

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_BypassCache_RunsTheFactoryEvenWhenCached()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), TimeSpan.FromMinutes(1));

        var bypassed = await cache.GetOrSetAsync("k", () => counter.Next("b"), TimeSpan.FromMinutes(1), bypassCache: true);

        Assert.Equal("b", bypassed);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_BypassCache_UpdatesTheCacheForLaterNormalCalls()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), TimeSpan.FromMinutes(1));
        await cache.GetOrSetAsync("k", () => counter.Next("b"), TimeSpan.FromMinutes(1), bypassCache: true);

        var later = await cache.GetOrSetAsync("k", () => counter.Next("c"), TimeSpan.FromMinutes(1));

        Assert.Equal("b", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_BypassCache_NullResult_DoesNotEvictTheCachedValue()
    {
        var cache = Build();
        var counter = new Counter();
        await cache.GetOrSetAsync("k", () => counter.Next("a"), TimeSpan.FromMinutes(1));

        var bypassed = await cache.GetOrSetAsync("k", () => counter.Next(null), TimeSpan.FromMinutes(1), bypassCache: true);
        var later = await cache.GetOrSetAsync("k", () => counter.Next("c"), TimeSpan.FromMinutes(1));

        Assert.Null(bypassed);
        Assert.Equal("a", later);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task GetOrSetAsync_BypassCache_OnAnEmptyCache_BehavesLikeANormalMiss()
    {
        var cache = Build();
        var counter = new Counter();

        var first = await cache.GetOrSetAsync("k", () => counter.Next("a"), TimeSpan.FromMinutes(1), bypassCache: true);
        var second = await cache.GetOrSetAsync("k", () => counter.Next("b"), TimeSpan.FromMinutes(1));

        Assert.Equal("a", first);
        Assert.Equal("a", second);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task ADefaultInterfaceImplementation_Bypasses_ButCannotWriteBack()
    {
        // 自訂的 ICacheService 只實作舊方法時，新的 overload 仍可用：略過快取＝直接執行 factory。
        ICacheService legacy = new LegacyCache();
        var counter = new Counter();

        var bypassed = await legacy.GetOrSetAsync("k", () => counter.Next("fresh"), null, bypassCache: true);
        var normal = await legacy.GetOrSetAsync("k", () => counter.Next("unused"), null, bypassCache: false);

        Assert.Equal("fresh", bypassed);
        Assert.Equal("from-legacy-cache", normal);
        Assert.Equal(1, counter.Calls);
    }

    private sealed class LegacyCache : ICacheService
    {
        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
            => Task.FromResult((T)(object)"from-legacy-cache");
    }
}
