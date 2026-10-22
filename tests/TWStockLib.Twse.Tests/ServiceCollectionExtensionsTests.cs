using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Abstractions;
using TWStockLib.Cache;
using TWStockLib.Services;

namespace TWStockLib.Twse.Tests;

public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection().AddLogging();
        register(services);
        // 與 ASP.NET Core 開發環境相同的檢查：scope 誤用、建置時解析失敗都會丟例外
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void AddTwStock_CalledTwice_RegistersEachMarketOnce()
    {
        using var provider = Build(services =>
        {
            services.AddTwStock();
            services.AddTwStock();
        });

        Assert.Equal(2, provider.GetServices<IStockDataSource>().Count());
        Assert.NotNull(provider.GetRequiredService<IStockMarketService>());
    }

    [Fact]
    public void AddTwStock_TheServiceIsShared_SoPriceSubscriptionsSurviveAcrossScopes()
    {
        using var provider = Build(services => services.AddTwStock());

        using var firstRequest = provider.CreateScope();
        using var secondRequest = provider.CreateScope();

        Assert.Same(
            firstRequest.ServiceProvider.GetRequiredService<IStockMarketService>(),
            secondRequest.ServiceProvider.GetRequiredService<IStockMarketService>());
    }

    private sealed class CustomCache : ICacheService
    {
        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) => factory();
        public Task<T> RefreshAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) => factory();
    }

    [Fact]
    public void AddTwStock_KeepsACacheServiceRegisteredBeforehand()
    {
        var custom = new CustomCache();
        using var provider = Build(services =>
        {
            services.AddSingleton<ICacheService>(custom);
            services.AddTwStock();
        });

        Assert.Same(custom, provider.GetRequiredService<ICacheService>());
    }
}
