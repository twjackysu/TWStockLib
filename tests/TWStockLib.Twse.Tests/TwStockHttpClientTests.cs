using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static TWStockLib.Twse.Tests.FakeHttpMessageHandler;
using static TWStockLib.Twse.Tests.FakeTimeProviderExtensions;

namespace TWStockLib.Twse.Tests;

/// <summary>AddTwStock 註冊的具名 HttpClient：以假的 primary handler 取代網路，離線驗證整條管線。</summary>
public class TwStockHttpClientTests
{
    private static ServiceProvider Build(FakeHttpMessageHandler network, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection().AddLogging();
        (register ?? (s => s.AddTwStock()))(services);
        services.AddHttpClient(TwStockOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => network);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task TheBuiltInSources_SendThroughTheNamedClient_WithALibraryUserAgent()
    {
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network);

        var result = await provider.GetRequiredService<IStockMarketService>().GetRealtimeQuoteAsync("2330", MarketType.TSE);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(network.Requests);
        Assert.StartsWith("TWStockLib/", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public void AddTwStock_CalledTwice_AddsTheUserAgentOnce()
    {
        using var provider = Build(new FakeHttpMessageHandler(), services =>
        {
            services.AddTwStock();
            services.AddTwStock();
        });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TwStockOptions.HttpClientName);

        Assert.Equal(2, client.DefaultRequestHeaders.UserAgent.Count); // 產品名稱 + 註解（專案網址）
        Assert.Equal(TimeSpan.FromSeconds(100), client.Timeout);
    }

    [Fact]
    public void TheNamedClient_AcceptsCompressedResponses()
    {
        var services = new ServiceCollection().AddLogging().AddTwStock();
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(TwStockOptions.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var decompression = handler switch
        {
            HttpClientHandler h => h.AutomaticDecompression,
            SocketsHttpHandler h => h.AutomaticDecompression,
            _ => throw new InvalidOperationException(handler.GetType().Name),
        };
        Assert.Equal(System.Net.DecompressionMethods.All, decompression);
    }

    [Fact]
    public void TheTimeout_CanBeChangedThroughTheNamedClient()
    {
        using var provider = Build(new FakeHttpMessageHandler(), services =>
        {
            services.AddTwStock();
            services.AddHttpClient(TwStockOptions.HttpClientName).ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TwStockOptions.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    [Fact]
    public void OtherClients_AreLeftAlone()
    {
        using var provider = Build(new FakeHttpMessageHandler());

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient();

        Assert.Empty(client.DefaultRequestHeaders.UserAgent);
        Assert.Equal(TimeSpan.FromSeconds(100), client.Timeout);
    }

    // ── 限流 ──────────────────────────────────────────────────────────────────

    private static HttpClient NamedClient(ServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(TwStockOptions.HttpClientName);

    [Fact]
    public async Task TheNamedClient_IsRateLimitedByDefault()
    {
        var time = new FakeTimeProvider();
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddTwStock();
        });
        var client = NamedClient(provider);
        for (var i = 0; i < 3; i++)
            await client.GetStringAsync(TwseHistoryUrl);

        var fourth = client.GetStringAsync(TwseHistoryUrl);

        Assert.False(fourth.IsCompleted);
        Assert.Equal(3, network.Requests.Count);
        time.Advance(TimeSpan.FromSeconds(5));
        await fourth.WaitAsync(RealTimeout);
        Assert.Equal(4, network.Requests.Count);
    }

    /// <summary>模擬 AddStandardResilienceHandler 之類的重試：每個請求實際送出兩次。</summary>
    private sealed class SendTwiceHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (await base.SendAsync(request, cancellationToken)).Dispose();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task RetriesAddedAfterAddTwStock_AreRateLimitedToo()
    {
        var time = new FakeTimeProvider();
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddTwStock();
            services.AddHttpClient(TwStockOptions.HttpClientName).AddHttpMessageHandler(() => new SendTwiceHandler());
        });
        var client = NamedClient(provider);
        await client.GetStringAsync(TwseHistoryUrl);

        // 第二次呼叫的第一次送出是第 3 個請求；重試（第 4 個）必須等限流，代表限流 handler 在重試 handler 內層
        var second = client.GetStringAsync(TwseHistoryUrl);

        Assert.False(second.IsCompleted);
        Assert.Equal(3, network.Requests.Count);
        time.Advance(TimeSpan.FromSeconds(5));
        await second.WaitAsync(RealTimeout);
        Assert.Equal(4, network.Requests.Count);
    }

    [Fact]
    public async Task TheRateLimit_CanBeDisabled()
    {
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network, services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            services.AddTwStock(o => o.RateLimit.Enabled = false);
        });
        var client = NamedClient(provider);

        for (var i = 0; i < 10; i++)
            await client.GetStringAsync(TwseHistoryUrl).WaitAsync(RealTimeout);

        Assert.Equal(10, network.Requests.Count);
    }

    [Fact]
    public async Task TheRateLimit_UsesTheConfiguredPermitsAndWindow()
    {
        var time = new FakeTimeProvider();
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddTwStock(o =>
            {
                o.RateLimit.PermitLimit = 1;
                o.RateLimit.Window = TimeSpan.FromSeconds(10);
            });
        });
        var client = NamedClient(provider);
        await client.GetStringAsync(TwseHistoryUrl);

        var second = client.GetStringAsync(TwseHistoryUrl);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(RealTimeout);
        Assert.Equal(2, network.Requests.Count);
    }

    [Fact]
    public async Task ALongHistoryRange_IsThrottledInsteadOfBurstingTheUpstream()
    {
        var time = new FakeTimeProvider();
        var network = new FakeHttpMessageHandler();
        using var provider = Build(network, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddTwStock();
        });
        var stock = provider.GetRequiredService<IStockMarketService>();

        // 四個月 = 四個請求，第四個要等第一個滑出 5 秒視窗
        var history = stock.GetHistoricalDataAsync("2330", MarketType.TSE, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30));
        var elapsed = await time.AdvanceUntilCompletedAsync(history, TimeSpan.FromMilliseconds(250));

        Assert.True((await history).IsSuccess);
        Assert.Equal(4, network.Requests.Count);
        Assert.True(elapsed >= TimeSpan.FromSeconds(5), $"只花了 {elapsed}");
    }
}
