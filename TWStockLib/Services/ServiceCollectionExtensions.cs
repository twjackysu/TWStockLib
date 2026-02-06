using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Cache;
using TWStockLib.Services;
using TWStockLib.Sources;

namespace TWStockLib
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTWStockClient(this IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddMemoryCache();
            
            // Core Services
            services.AddSingleton<ICacheService, MemoryCacheService>();
            
            // Sources
            services.AddSingleton<IStockSource, TwseSource>();
            services.AddSingleton<IStockSource, TpexSource>();
            
            // Client
            services.AddSingleton<ITWStockClient, TWStockClient>();
            
            return services;
        }
    }
}
