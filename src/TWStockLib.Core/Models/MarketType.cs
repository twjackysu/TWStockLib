namespace TWStockLib.Models
{
    public enum MarketType
    {
        /// <summary>上櫃</summary>
        [MarketKey("otc_{Symbol}.tw")]
        OTC,
        /// <summary>上市</summary>
        [MarketKey("tse_{Symbol}.tw")]
        TSE,
    }
} 