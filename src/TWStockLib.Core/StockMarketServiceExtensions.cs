using TWStockLib.Models;

namespace TWStockLib.Services;

/// <summary><see cref="IStockMarketService"/> 的便利多載。</summary>
public static class StockMarketServiceExtensions
{
    /// <summary>以預設選項取得即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        this IStockMarketService service, string symbol, MarketType market, CancellationToken ct)
        => service.GetRealtimeQuoteAsync(symbol, market, QuoteOptions.Default, ct);
}
