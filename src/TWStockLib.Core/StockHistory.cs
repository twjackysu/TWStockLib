namespace TWStockLib;

/// <summary>單日歷史行情。上市與上櫃的成交量一律為「股」、成交金額一律為「元」。</summary>
public sealed record StockHistory
{
    /// <summary>交易日</summary>
    public required DateOnly Date { get; init; }
    /// <summary>成交股數</summary>
    public long TradeVolume { get; init; }
    /// <summary>成交金額（元）</summary>
    public decimal TurnoverValue { get; init; }
    /// <summary>開盤價；當日無成交時為 <c>null</c>。</summary>
    public decimal? OpeningPrice { get; init; }
    /// <summary>最高價；當日無成交時為 <c>null</c>。</summary>
    public decimal? HighestPrice { get; init; }
    /// <summary>最低價；當日無成交時為 <c>null</c>。</summary>
    public decimal? LowestPrice { get; init; }
    /// <summary>收盤價；當日無成交時為 <c>null</c>。</summary>
    public decimal? ClosingPrice { get; init; }
    /// <summary>漲跌價差；除權息等不比價的日子為 <c>null</c>。</summary>
    public decimal? Change { get; init; }
    /// <summary>成交筆數</summary>
    public long NumberOfDeals { get; init; }
}
