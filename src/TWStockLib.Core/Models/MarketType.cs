namespace TWStockLib.Models;

/// <summary>台灣股票市場別。</summary>
public enum MarketType
{
    /// <summary>未指定。沒有任何資料來源負責它，查詢會回傳 <c>SOURCE_NOT_FOUND</c>。</summary>
    Unknown = 0,
    /// <summary>上市（台灣證券交易所，TSE）。</summary>
    TSE = 1,
    /// <summary>上櫃（櫃買中心，TPEX）。</summary>
    OTC = 2,
}
