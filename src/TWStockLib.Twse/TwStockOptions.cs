namespace TWStockLib;

/// <summary>
/// 內建證交所 / 櫃買資料來源的設定，以 <c>services.AddTwStock(options =&gt; …)</c> 調整。
/// 預設值與 3.0 的行為相同，只需覆寫想改的項目。
/// </summary>
public sealed class TwStockOptions
{
    /// <summary>
    /// 內建資料來源使用的具名 <see cref="HttpClient"/>。以 <c>services.AddHttpClient(TwStockOptions.HttpClientName)</c>
    /// 取得同一個 <c>IHttpClientBuilder</c>，即可加上重試等韌性處理（例如 <c>AddStandardResilienceHandler()</c>）、
    /// 調整逾時或設定 Proxy。預設逾時 100 秒，User-Agent 為 <c>TWStockLib/{版本}</c>。
    /// </summary>
    public const string HttpClientName = "TWStockLib";

    /// <summary>即時報價的快取時間，預設 30 秒。</summary>
    public TimeSpan QuoteCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>股票清單的快取時間，預設 1 天。</summary>
    public TimeSpan StockListCacheDuration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>已結束月份（以台灣時間判斷）歷史資料的快取時間，預設 1 天；這些資料不再變動。</summary>
    public TimeSpan ClosedMonthHistoryCacheDuration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// 當月與未來月份歷史資料的快取時間，預設 30 分鐘。當月每個交易日收盤後會多一列，
    /// 快取太久會一直拿不到最新一天的資料。
    /// </summary>
    public TimeSpan CurrentMonthHistoryCacheDuration { get; set; } = TimeSpan.FromMinutes(30);
}
