namespace TWStockLib.Observer
{
    public class StockPriceSubject
    {
        private readonly Dictionary<string, List<IStockPriceObserver>> _observers = new();
        private readonly Dictionary<string, decimal> _lastPrices = new();

        public void Subscribe(string symbol, IStockPriceObserver observer)
        {
            if (!_observers.ContainsKey(symbol))
            {
                _observers[symbol] = new List<IStockPriceObserver>();
            }
            _observers[symbol].Add(observer);
        }

        public void Unsubscribe(string symbol, IStockPriceObserver observer)
        {
            if (_observers.ContainsKey(symbol))
            {
                _observers[symbol].Remove(observer);
            }
        }

        public void UpdatePrice(string symbol, decimal newPrice)
        {
            if (_lastPrices.TryGetValue(symbol, out decimal oldPrice))
            {
                if (oldPrice != newPrice && _observers.ContainsKey(symbol))
                {
                    foreach (var observer in _observers[symbol])
                    {
                        observer.OnPriceChanged(symbol, newPrice, oldPrice);
                    }
                }
            }
            _lastPrices[symbol] = newPrice;
        }
    }
} 