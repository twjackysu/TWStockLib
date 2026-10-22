namespace TWStockLib;

/// <summary>即時報價。數量單位為「張」。</summary>
public sealed record StockQuote
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required MarketType Market { get; init; }

    /// <summary>最新成交價；盤前尚未成交時為 <c>null</c>。</summary>
    public decimal? LastPrice { get; init; }
    /// <summary>最新一筆成交量（張）。</summary>
    public long? LastVolume { get; init; }
    /// <summary>今日累積成交量（張）。</summary>
    public long? TotalVolume { get; init; }
    /// <summary>最佳五檔委賣，價格由低到高。</summary>
    public IReadOnlyList<PriceLevel> Asks { get; init; } = [];
    /// <summary>最佳五檔委買，價格由高到低。</summary>
    public IReadOnlyList<PriceLevel> Bids { get; init; } = [];
    /// <summary>上游資料時間（台灣時間，+08:00）。</summary>
    public DateTimeOffset? SyncTime { get; init; }
    /// <summary>最高價</summary>
    public decimal? HighestPrice { get; init; }
    /// <summary>最低價</summary>
    public decimal? LowestPrice { get; init; }
    /// <summary>開盤價</summary>
    public decimal? OpeningPrice { get; init; }
    /// <summary>昨日收盤價</summary>
    public decimal? YesterdayClosingPrice { get; init; }
    /// <summary>漲停價；指數等沒有漲跌停的標的為 <c>null</c>。</summary>
    public decimal? LimitUp { get; init; }
    /// <summary>跌停價；指數等沒有漲跌停的標的為 <c>null</c>。</summary>
    public decimal? LimitDown { get; init; }
}
