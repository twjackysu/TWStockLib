namespace TWStockLib;

/// <summary>
/// 證券類型。內建資料來源依 ISIN 頁面的 CFICode（ISO 10962）判斷，
/// 對照見各成員說明；需要更細的分類（例如認購 / 認售權證）時可直接看 <see cref="StockData.CfiCode"/>。
/// </summary>
public enum SecurityType
{
    /// <summary>未分類：資料來源沒有提供證券類型。</summary>
    Unknown = 0,
    /// <summary>普通股，含創新板與 KY 股（CFI <c>ES</c>）。</summary>
    CommonStock = 1,
    /// <summary>特別股（CFI <c>EP</c>、可轉換特別股 <c>EF</c>）。</summary>
    PreferredStock = 2,
    /// <summary>臺灣存託憑證 TDR（CFI <c>ED</c>）。</summary>
    DepositaryReceipt = 3,
    /// <summary>ETF（CFI <c>CE</c>）。</summary>
    Etf = 4,
    /// <summary>ETN（CFI <c>CM</c>；證交所與櫃買目前只把 ETN 編為 <c>CMXXXU</c>）。</summary>
    Etn = 5,
    /// <summary>受益證券：不動產投資信託 REIT（CFI <c>CB</c>）與資產基礎證券（CFI <c>DA</c>）。</summary>
    BeneficiarySecurity = 6,
    /// <summary>權證，含認購、認售與牛熊證（CFI <c>RW</c>）。</summary>
    Warrant = 7,
    /// <summary>債券（CFI <c>D</c> 開頭，資產基礎證券除外）。</summary>
    Bond = 8,
    /// <summary>有 CFICode，但不屬於以上任何一類。</summary>
    Other = 9,
}
