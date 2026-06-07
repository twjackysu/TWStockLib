using TWStockLib.Models;

namespace TWStockLib.Twse.Parsers;

/// <summary>
/// 將證交所家族端點的原始回應字串轉成強型別模型。純函式、不碰 IO，可餵固定 fixture 單元測試。
/// </summary>
public interface IStockParser
{
    /// <summary>解析 mis.twse.com.tw 即時報價 JSON（msgArray）。查無資料回 <c>null</c>。</summary>
    StockQuote? ParseRealtimeQuote(string json, MarketType market);

    /// <summary>解析證交所 STOCK_DAY 歷史 JSON（TSE）。</summary>
    IReadOnlyList<StockHistory> ParseTwseHistory(string json);

    /// <summary>解析櫃買 st43_result 歷史 JSON（OTC）。</summary>
    IReadOnlyList<StockHistory> ParseTpexHistory(string json);

    /// <summary>解析 ISIN 股票清單頁（Big5 已解碼的 HTML）。</summary>
    IReadOnlyDictionary<string, StockData> ParseStockList(string html, MarketType market, bool includeWarrant);
}
