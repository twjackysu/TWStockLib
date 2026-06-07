namespace TWStockLib.Observer.DefaultProvidedObservers
{
    public class ConsoleStockPriceObserver : IStockPriceObserver
    {
        private readonly string _name;

        public ConsoleStockPriceObserver(string name)
        {
            _name = name;
        }

        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
        {
            var changePercentage = (newPrice - oldPrice) / oldPrice * 100;
            var direction = newPrice > oldPrice ? "上漲" : "下跌";

            Console.WriteLine($"[{_name}] 股票 {symbol} {direction}: 從 {oldPrice} 到 {newPrice} ({changePercentage:F2}%)");
        }
    }
}
