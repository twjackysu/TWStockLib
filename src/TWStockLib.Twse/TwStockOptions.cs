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

    /// <summary>內建限流，避免長區間的歷史查詢或多檔 <c>WatchAsync</c> 讓上游封鎖呼叫端的 IP。</summary>
    public TwStockRateLimitOptions RateLimit { get; } = new();
}

/// <summary>
/// 內建限流設定。每個上游主機（www.twse.com.tw、mis.twse.com.tw、www.tpex.org.tw、isin.twse.com.tw）分開計算：
/// 任何長度為 <see cref="Window"/> 的區間內最多送出 <see cref="PermitLimit"/> 個請求，超過的請求排隊等候，
/// 不會失敗。排隊時間算在 HttpClient 的逾時內，呼叫端可用 <see cref="CancellationToken"/> 放棄等候。
/// </summary>
/// <remarks>
/// 預設每 5 秒 3 個請求，取自社群經驗（證交所未公告正式數字）：超過這個頻率，證交所會暫時封鎖來源 IP。
/// 限流 handler 位於具名 HttpClient 管線的最內層，自行加上的重試每一次實際送出也都會計入。
/// </remarks>
public sealed class TwStockRateLimitOptions
{
    /// <summary>是否啟用內建限流，預設啟用。已在其他地方限流（例如自己的 handler 或 Proxy）時可關閉。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>每個主機在 <see cref="Window"/> 內最多送出的請求數，預設 3。</summary>
    public int PermitLimit { get; set; } = 3;

    /// <summary>計算 <see cref="PermitLimit"/> 的滑動視窗長度，預設 5 秒。</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(5);
}
