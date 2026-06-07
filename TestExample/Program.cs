using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using TWStockLib.Models;
using TWStockLib.Observer;
using TWStockLib.Services;
using System.Text;
using TWStockLib.Observer.DefaultProvidedObservers;

namespace TestExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 註冊編碼提供者，以支援 950 (繁體中文 Big5) 編碼
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            var logger = LogManager.GetCurrentClassLogger();
            try
            {
                var config = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                   .Build();

                var servicesProvider = BuildDi(config);
                using (servicesProvider as IDisposable)
                {
                    var stockMarketService = servicesProvider.GetRequiredService<IStockMarketService>();

                    // 獲取股票清單
                    logger.Info("獲取股票清單...");
                    var allStocksResult = await stockMarketService.GetAllStockListAsync();
                    var allStocks = allStocksResult.IsSuccess
                        ? allStocksResult.Value
                        : new Dictionary<string, StockData>();
                    logger.Info($"總共獲取到 {allStocks.Count} 支股票");

                    // 獲取歷史數據 006208
                    logger.Info("獲取歷史數據...");
                    var tseHistory = await stockMarketService.GetHistoricalDataAsync(
                        "006208",
                        new DateTime(2019, 11, 1),
                        new DateTime(2019, 11, 30),
                        MarketType.TSE);
                    logger.Info($"006208 歷史數據: {tseHistory.Value?.Count ?? 0} 筆");

                    // 獲取歷史數據 00687B
                    logger.Info("嘗試獲取 00687B 的歷史數據...");
                    var otcHistory = await stockMarketService.GetHistoricalDataAsync(
                        "00687B",
                        new DateTime(2023, 11, 1),
                        new DateTime(2023, 11, 30),
                        MarketType.OTC);
                    logger.Info($"00687B 歷史數據: {otcHistory.Value?.Count ?? 0} 筆");
                    
                    // 獲取即時報價
                    logger.Info("獲取即時報價...");
                    var searchStockList = new string[] { "2439", "2330", "2317", "3679", "3548", "4942" };

                    // 創建一個自定義價格觀察者
                    var observer = new TestObserver(logger);

                    // 訂閱價格變化
                    foreach (var symbol in searchStockList)
                    {
                        stockMarketService.SubscribePriceChanges(symbol, observer);
                    }
                    // 等5秒看有沒有變化
                    Task.Delay(5000).Wait();
                    // 取消訂閱
                    foreach (var symbol in searchStockList)
                    {
                        stockMarketService.UnsubscribePriceChanges(symbol, observer);
                    }

                    // 獲取即時報價
                    foreach (var symbol in searchStockList)
                    {
                        var marketType = allStocks.TryGetValue(symbol, out var stock) 
                            ? stock.Market 
                            : MarketType.TSE;
                            
                        var quoteResult = await stockMarketService.GetRealtimeQuoteAsync(symbol, marketType);
                        if (quoteResult.IsSuccess)
                        {
                            var quote = quoteResult.Value;
                            logger.Info($"{symbol} {quote.Name} 目前價格: {quote.LastPrice}");
                        }
                        else
                        {
                            logger.Warn($"{symbol} 無法獲取報價: {quoteResult.ErrorMessage}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // NLog: catch any exception and log it.
                logger.Error(ex, "Stopped program because of exception");
                throw;
            }
            finally
            {
                logger.Info("Stock Test Example End");
                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                LogManager.Shutdown();
            }
        }
        
        private static IServiceProvider BuildDi(IConfiguration config)
        {
            var services = new ServiceCollection();
            
            // 添加日誌
            services.AddLogging(loggingBuilder =>
            {
                // configure Logging with NLog
                loggingBuilder.ClearProviders();
                loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                loggingBuilder.AddNLog(config);
            });
            
            // 添加股票服務
            services.AddTwStock();
            
            return services.BuildServiceProvider();
        }
    }
    
    // 自定義觀察者實現
    public class TestObserver : IStockPriceObserver
    {
        private readonly Logger _logger;
        
        public TestObserver(Logger logger)
        {
            _logger = logger;
        }
        
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
        {
            var changePercentage = (newPrice - oldPrice) / oldPrice * 100;
            var direction = newPrice > oldPrice ? "上漲" : "下跌";
            
            _logger.Info($"股票 {symbol} {direction}: 從 {oldPrice} 到 {newPrice} ({changePercentage:F2}%)");
        }
    }
}
