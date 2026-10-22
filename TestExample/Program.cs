using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using TWStockLib.Models;
using TWStockLib.Services;

namespace TestExample;

class Program
{
    static async Task Main(string[] args)
    {
        var logger = LogManager.GetCurrentClassLogger();
        try
        {
            var config = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
               .Build();

            await using var servicesProvider = BuildDi(config);
            var stockMarketService = servicesProvider.GetRequiredService<IStockMarketService>();

            // 獲取股票清單
            logger.Info("獲取股票清單...");
            var allStocksResult = await stockMarketService.GetAllStockListAsync();
            IReadOnlyDictionary<string, StockData> allStocks = allStocksResult.IsSuccess
                ? allStocksResult.Value
                : new Dictionary<string, StockData>();
            logger.Info($"總共獲取到 {allStocks.Count} 支股票");

            // 獲取歷史數據 006208
            logger.Info("獲取歷史數據...");
            var tseHistory = await stockMarketService.GetHistoricalDataAsync(
                "006208", MarketType.TSE, new DateOnly(2019, 11, 1), new DateOnly(2019, 11, 30));
            logger.Info($"006208 歷史數據: {tseHistory.Value?.Count ?? 0} 筆");

            // 獲取歷史數據 00687B
            logger.Info("嘗試獲取 00687B 的歷史數據...");
            var otcHistory = await stockMarketService.GetHistoricalDataAsync(
                "00687B", MarketType.OTC, new DateOnly(2023, 11, 1), new DateOnly(2023, 11, 30));
            logger.Info($"00687B 歷史數據: {otcHistory.Value?.Count ?? 0} 筆");

            // 獲取即時報價
            logger.Info("獲取即時報價...");
            var searchStockList = new[] { "2439", "2330", "2317", "3679", "3548", "4942" };
            foreach (var symbol in searchStockList)
            {
                var marketType = allStocks.TryGetValue(symbol, out var stock) ? stock.Market : MarketType.TSE;

                var quoteResult = await stockMarketService.GetRealtimeQuoteAsync(symbol, marketType);
                if (quoteResult.IsSuccess)
                    logger.Info($"{symbol} {quoteResult.Value.Name} 目前價格: {quoteResult.Value.LastPrice}");
                else
                    logger.Warn($"{symbol} 無法獲取報價: {quoteResult.ErrorCode} {quoteResult.ErrorMessage}");
            }

            // 追蹤 2330 的成交價 30 秒：先回傳目前報價，之後只在價格變動時回傳
            logger.Info("追蹤 2330 成交價 30 秒...");
            using var watching = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await foreach (var quote in stockMarketService.WatchAsync("2330", MarketType.TSE, ct: watching.Token))
                    logger.Info($"{quote.Symbol} {quote.Name} {quote.SyncTime:HH:mm:ss} 成交價 {quote.LastPrice}");
            }
            catch (OperationCanceledException)
            {
                logger.Info("追蹤結束");
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

    private static ServiceProvider BuildDi(IConfiguration config)
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
