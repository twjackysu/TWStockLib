namespace TWStockLib.Models
{
    [AttributeUsage(AttributeTargets.Field)]
    public class MarketKeyAttribute : Attribute
    {
        public string Key;
        public MarketKeyAttribute(string key)
        {
            Key = key;
        }
    }
} 