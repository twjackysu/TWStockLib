using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using TWStockLib;
using TWStockLib.Abstractions;
using TWStockLib.Twse.DataSources;
using TWStockLib.Twse.Http;

// 與 AddHttpClient、AddMemoryCache 等擴充方法放在同一個命名空間，註冊時不必另外 using
namespace Microsoft.Extensions.DependencyInjection;

public static class TwStockServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 TWStockLib 證交所（TSE）/ 櫃買（OTC）資料服務。重複呼叫不會重複註冊；
    /// 呼叫前已註冊的 <see cref="ICacheService"/> 等相依會被保留，可藉此替換預設實作。
    /// </summary>
    public static IServiceCollection AddTwStock(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICacheService, MemoryCacheService>();

        services.AddOptions<TwStockOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TwStockOptions>, TwStockOptionsValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HttpClientFactoryOptions>, TwStockHttpClientSetup>());
        services.TryAddSingleton<IStockHttpFetcher, TwseHttpFetcher>();

        // 每市場一個資料來源；新增市場只多註冊一行（OCP）
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TwseStockDataSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TpexStockDataSource>());

        services.TryAddSingleton<IStockMarketService, StockMarketService>();
        return services;
    }

    /// <summary>
    /// 註冊 TWStockLib 資料服務，並以 <paramref name="configure"/> 調整快取時間等設定（見 <see cref="TwStockOptions"/>）。
    /// 可重複呼叫，每次的 <paramref name="configure"/> 依呼叫順序套用；設定不合法時，第一次取得服務會拋出
    /// <see cref="OptionsValidationException"/>。
    /// </summary>
    public static IServiceCollection AddTwStock(this IServiceCollection services, Action<TwStockOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTwStock();
        services.Configure(configure);
        return services;
    }
}
