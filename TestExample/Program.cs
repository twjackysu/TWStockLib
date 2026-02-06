using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using TWStockLib.Models;
using TWStockLib.Observer;
using TWStockLib.Services;
using System.Text;
using TWStockLib;

namespace TestExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            var logger = LogManager.GetCurrentClassLogger();
            try
            {
                var config = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                   .Build();

                var servicesProvider = BuildDi(config);
                
                // Use the new Interface
                var client = servicesProvider.GetRequiredService<ITWStockClient>();

                // 獲取股票清單
                logger.Info("獲取股票清單...");
                var allStocks = await client.GetAllStockListAsync();
                logger.Info($"總共獲取到 {allStocks.Count} 支股票");

                // 獲取歷史數據
                logger.Info("獲取歷史數據 006208 (TSE)...");
                var tseHistory = await client.GetHistoricalDataAsync(
                    "006208", 
                    new DateTime(2019, 11, 1), 
                    new DateTime(2019, 11, 30));
                logger.Info($"006208 歷史數據: {tseHistory.Count()} 筆");

                logger.Info("獲取歷史數據 00687B (OTC)...");
                var otcHistory = await client.GetHistoricalDataAsync(
                    "00687B", 
                    new DateTime(2023, 11, 1), 
                    new DateTime(2023, 11, 30));
                logger.Info($"00687B 歷史數據: {otcHistory.Count()} 筆");
                
                // 獲取即時報價
                logger.Info("獲取即時報價...");
                var searchStockList = new string[] { "2439", "2330", "2317", "3679", "3548", "4942" };

                var observer = new TestObserver(logger);

                foreach (var symbol in searchStockList)
                {
                    client.SubscribeMonitor(symbol, observer);
                }
                
                Task.Delay(5000).Wait();
                
                foreach (var symbol in searchStockList)
                {
                    client.UnsubscribeMonitor(symbol, observer);
                }

                foreach (var symbol in searchStockList)
                {
                    var quote = await client.GetRealtimeQuoteAsync(symbol);
                    if (quote != null)
                    {
                        logger.Info($"{symbol} {quote.Name} 目前價格: {quote.LastPrice}");
                    }
                    else
                    {
                        logger.Warn($"{symbol} 無法獲取報價");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Stopped program because of exception");
                throw;
            }
            finally
            {
                logger.Info("Stock Test Example End");
                LogManager.Shutdown();
            }
        }
        
        private static IServiceProvider BuildDi(IConfiguration config)
        {
            var services = new ServiceCollection();
            
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                loggingBuilder.AddNLog(config);
            });
            
            // New Registration
            services.AddTWStockClient();
            
            return services.BuildServiceProvider();
        }
    }
    
    public class TestObserver : IStockPriceObserver
    {
        private readonly Logger _logger;
        public TestObserver(Logger logger) => _logger = logger;
        
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
        {
            var changePercentage = oldPrice != 0 ? (newPrice - oldPrice) / oldPrice * 100 : 0;
            var direction = newPrice > oldPrice ? "上漲" : "下跌";
            _logger.Info($"股票 {symbol} {direction}: 從 {oldPrice} 到 {newPrice} ({changePercentage:F2}%)");
        }
    }
}
