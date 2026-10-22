namespace TWStockLib;

/// <summary><see cref="IStockMarketService"/> 的便利多載。</summary>
public static class StockMarketServiceExtensions
{
    /// <summary>以預設選項取得即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        this IStockMarketService service, string symbol, MarketType market, CancellationToken ct)
        => service.GetRealtimeQuoteAsync(symbol, market, QuoteOptions.Default, ct);

    /// <summary>不指定市場、以預設選項取得即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        this IStockMarketService service, string symbol, CancellationToken ct)
        => service.GetRealtimeQuoteAsync(symbol, QuoteOptions.Default, ct);

    /// <summary>不指定市場、以預設選項一次取得多檔即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        this IStockMarketService service, IEnumerable<string> symbols, CancellationToken ct)
        => service.GetRealtimeQuotesAsync(symbols, QuoteOptions.Default, ct);

    /// <summary>以預設選項一次取得多檔即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
        this IStockMarketService service, IEnumerable<(string Symbol, MarketType Market)> symbols, CancellationToken ct)
        => service.GetRealtimeQuotesAsync(symbols, QuoteOptions.Default, ct);
}
