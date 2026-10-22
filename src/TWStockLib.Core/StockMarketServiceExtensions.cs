using System.Collections.Frozen;

namespace TWStockLib;

/// <summary><see cref="IStockMarketService"/> 的便利多載。</summary>
public static class StockMarketServiceExtensions
{
    /// <summary>以預設選項取得即時報價，只傳 <see cref="CancellationToken"/>。</summary>
    public static Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        this IStockMarketService service, string symbol, MarketType market, CancellationToken ct)
        => service.GetRealtimeQuoteAsync(symbol, market, QuoteOptions.Default, ct);

    /// <summary>
    /// 取得指定市場中屬於 <paramref name="securityTypes"/> 的證券，例如只要普通股與 ETF：
    /// <c>GetStockListAsync(MarketType.TSE, [SecurityType.CommonStock, SecurityType.Etf])</c>。
    /// 從 <c>includeWarrant</c> 多載使用的同一份清單過濾，不會多下載；資料來源沒有提供類型的項目
    /// （<see cref="SecurityType.Unknown"/>）只有在 <paramref name="securityTypes"/> 包含它時才會出現。
    /// </summary>
    public static Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        this IStockMarketService service, MarketType market, IReadOnlyCollection<SecurityType> securityTypes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(securityTypes);

        var wanted = securityTypes.ToHashSet();
        return FilterAsync(service.GetStockListAsync(market, wanted.Contains(SecurityType.Warrant), ct), wanted);
    }

    /// <summary>
    /// 取得上市 + 上櫃中屬於 <paramref name="securityTypes"/> 的證券，例如全市場的 ETF：
    /// <c>GetAllStockListAsync([SecurityType.Etf])</c>。過濾方式同
    /// <see cref="GetStockListAsync(IStockMarketService, MarketType, IReadOnlyCollection{SecurityType}, CancellationToken)"/>。
    /// </summary>
    public static Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        this IStockMarketService service, IReadOnlyCollection<SecurityType> securityTypes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(securityTypes);

        var wanted = securityTypes.ToHashSet();
        return FilterAsync(service.GetAllStockListAsync(wanted.Contains(SecurityType.Warrant), ct), wanted);
    }

    private static async Task<StockResult<IReadOnlyDictionary<string, StockData>>> FilterAsync(
        Task<StockResult<IReadOnlyDictionary<string, StockData>>> query, HashSet<SecurityType> wanted)
    {
        var result = await query;
        return result.TryGetValue(out var list)
            ? StockResult<IReadOnlyDictionary<string, StockData>>.Ok(
                list.Where(p => wanted.Contains(p.Value.SecurityType)).ToFrozenDictionary())
            : result;
    }
}
