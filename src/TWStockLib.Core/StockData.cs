namespace TWStockLib;

/// <summary>股票清單中的一檔：代號、名稱與市場別，以及 ISIN 頁面上的證券類型、產業別、ISIN 與上市日。</summary>
public sealed record StockData
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required MarketType Market { get; init; }

    /// <summary>證券類型；資料來源沒有提供時為 <see cref="SecurityType.Unknown"/>。</summary>
    public SecurityType SecurityType { get; init; }

    /// <summary>產業別，例如「半導體業」；ETF、權證等沒有產業別的證券為 <c>null</c>。</summary>
    public string? Industry { get; init; }

    /// <summary>國際證券辨識號碼，例如 <c>TW0002330008</c>；KY 股為發行地的 ISIN（如 <c>KYG898431096</c>）。</summary>
    public string? Isin { get; init; }

    /// <summary>上市（櫃）日。</summary>
    public DateOnly? ListedDate { get; init; }

    /// <summary>
    /// ISO 10962 CFI 代碼，例如普通股 <c>ESVUFR</c>、ETF <c>CEOGEU</c>、認購權證 <c>RWSCCA</c>。
    /// <see cref="SecurityType"/> 由它推得，需要更細的分類時可以直接看這個欄位。
    /// </summary>
    public string? CfiCode { get; init; }
}
