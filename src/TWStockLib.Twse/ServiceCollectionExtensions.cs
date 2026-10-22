using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TWStockLib.Abstractions;
using TWStockLib.Cache;
using TWStockLib.Twse.DataSources;
using TWStockLib.Twse.Http;

namespace TWStockLib.Services;

public static class ServiceCollectionExtensions
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

        services.TryAddSingleton<IStockHttpFetcher, TwseHttpFetcher>();

        // 每市場一個資料來源；新增市場只多註冊一行（OCP）
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TwseStockDataSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TpexStockDataSource>());

        // 價格訂閱存在服務實例上，必須是 Singleton，訂閱才不會隨 scope（例如每個 HTTP request）消失
        services.TryAddSingleton<IStockMarketService, StockMarketService>();
        return services;
    }

    /// <summary>
    /// 舊名稱，保留相容。請改用 <see cref="AddTwStock"/>。
    /// </summary>
    [Obsolete("請改用 AddTwStock()。此方法將於未來版本移除。")]
    public static IServiceCollection AddStockServices(this IServiceCollection services)
        => services.AddTwStock();
}
