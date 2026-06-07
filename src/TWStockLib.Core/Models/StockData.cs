namespace TWStockLib.Models
{
    public class StockData
    {
        public required string Symbol { get; set; }  // 原本的 No
        public required string Name { get; set; }
        public required MarketType Market { get; set; }  // 原本的 StockType
    }
} 