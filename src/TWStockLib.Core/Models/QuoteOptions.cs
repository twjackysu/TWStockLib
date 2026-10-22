namespace TWStockLib.Models;

/// <summary>即時報價的查詢選項。</summary>
public sealed record QuoteOptions
{
    /// <summary>預設：使用函式庫內部 30 秒的報價快取。</summary>
    public static QuoteOptions Default { get; } = new();

    /// <summary>略過內部快取、一律向上游查詢；查到的新報價仍會更新快取。</summary>
    public static QuoteOptions NoCache { get; } = new() { BypassCache = true };

    /// <summary>
    /// 為 <c>true</c> 時不使用內部快取，一律向上游查詢。呼叫端自己已有節流、或需要「這一刻」的值時使用；
    /// 上游有頻率限制，略過快取後的呼叫頻率由呼叫端負責。
    /// </summary>
    public bool BypassCache { get; init; }
}
