using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Cache;
using TWStockLib.Factory;

namespace TWStockLib.Services
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 註冊 TWStockLib 證交所（TSE）/ 櫃買（OTC）資料服務。
        /// </summary>
        public static IServiceCollection AddTwStock(this IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();
            services.AddSingleton<IStockMarketFactory, TwseMarketFactory>();
            services.AddScoped<IStockMarketService, StockMarketService>();
            return services;
        }

        /// <summary>
        /// 舊名稱，保留相容。請改用 <see cref="AddTwStock"/>。
        /// </summary>
        [Obsolete("請改用 AddTwStock()。此方法將於未來版本移除。")]
        public static IServiceCollection AddStockServices(this IServiceCollection services)
            => services.AddTwStock();
    }
}
