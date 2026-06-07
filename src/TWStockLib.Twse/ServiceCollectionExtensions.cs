using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Abstractions;
using TWStockLib.Cache;
using TWStockLib.Twse.DataSources;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

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

            services.AddSingleton<IStockHttpFetcher, TwseHttpFetcher>();
            services.AddSingleton<IStockParser, TwseStockParser>();

            // 每市場一個資料來源；新增市場只多註冊一行（OCP）
            services.AddSingleton<IStockDataSource, TwseStockDataSource>();
            services.AddSingleton<IStockDataSource, TpexStockDataSource>();

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
