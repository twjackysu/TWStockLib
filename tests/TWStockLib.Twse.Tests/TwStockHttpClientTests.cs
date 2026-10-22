using Microsoft.Extensions.DependencyInjection;

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
}
