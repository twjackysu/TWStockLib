namespace TWStockLib;

/// <summary>最佳五檔中的一檔：委託價格與張數。</summary>
public readonly record struct PriceLevel(decimal Price, long Volume);
