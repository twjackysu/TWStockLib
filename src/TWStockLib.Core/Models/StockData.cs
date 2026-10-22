namespace TWStockLib.Models;

public class StockData
{
    public required string Symbol { get; set; }  // 原本的 No
    public required string Name { get; set; }
    public required MarketType Market { get; set; }  // 原本的 StockType

    /// <summary>複製一份獨立的實例，讓呼叫端拿到的物件與函式庫快取中的共用物件脫鉤。</summary>
    internal virtual StockData Copy() => (StockData)MemberwiseClone();
}
