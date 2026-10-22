namespace TWStockLib;

/// <summary>股票清單的查詢選項。</summary>
public sealed record StockListOptions
{
    /// <summary>預設：不含權證，從 <see cref="StockListSource.OpenApi"/> 取得。</summary>
    public static StockListOptions Default { get; } = new();

    /// <summary>包含權證（認購、認售與牛熊證），從 <see cref="StockListSource.OpenApi"/> 取得。</summary>
    public static StockListOptions WithWarrants { get; } = new() { IncludeWarrant = true };

    /// <summary>為 <c>true</c> 時包含權證（認購、認售與牛熊證）。</summary>
    public bool IncludeWarrant { get; init; }

    /// <summary>
    /// 清單的上游，預設 <see cref="StockListSource.OpenApi"/>（快，但少數證券不在其中）；
    /// 需要最完整的清單時用 <see cref="StockListSource.Isin"/>（慢）。自訂的資料來源可以不理會這個選項。
    /// </summary>
    public StockListSource Source { get; init; }
}

/// <summary>股票清單的上游，見 <see cref="StockListOptions.Source"/>。</summary>
public enum StockListSource
{
    /// <summary>
    /// 證交所與櫃買的 OpenAPI（預設）：壓縮傳輸的 JSON，通常 1 秒內完成。只列出有行情資料的證券，
    /// 因此不含停止交易中、尚未掛牌的證券；上市清單在收盤後才更新，新掛牌的證券晚一個交易日出現。
    /// 權證只列出還沒過最後交易日的。
    /// </summary>
    OpenApi = 0,

    /// <summary>
    /// ISIN 證券編碼頁：最完整，含停止交易中與即將掛牌的證券，但每個市場是一頁數 MB 的網頁
    /// （上市約 9.7 MB），上游傳得慢，實測 6～78 秒。
    /// </summary>
    Isin = 1,
}
