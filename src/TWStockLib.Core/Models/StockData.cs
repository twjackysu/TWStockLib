namespace TWStockLib.Models;

/// <summary>股票清單中的一檔：代號、名稱與市場別。</summary>
public sealed record StockData
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required MarketType Market { get; init; }
}
