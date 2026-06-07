namespace TWStockLib.Observer
{
    public interface IStockPriceObserver
    {
        void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice);
    }
} 