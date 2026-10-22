namespace TWStockLib.Observer;

/// <summary>
/// 依股票代號管理價格觀察者。可多執行緒同時使用；通知在鎖外、對觀察者清單的快照進行，
/// 觀察者在回呼中訂閱或取消訂閱都安全。
/// </summary>
public class StockPriceSubject
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<IStockPriceObserver>> _observers = new();
    private readonly Dictionary<string, decimal> _lastPrices = new();

    public void Subscribe(string symbol, IStockPriceObserver observer)
    {
        lock (_gate)
        {
            if (!_observers.TryGetValue(symbol, out var observers))
            {
                observers = new List<IStockPriceObserver>();
                _observers[symbol] = observers;
            }
            observers.Add(observer);
        }
    }

    public void Unsubscribe(string symbol, IStockPriceObserver observer)
    {
        lock (_gate)
        {
            if (_observers.TryGetValue(symbol, out var observers))
            {
                observers.Remove(observer);
            }
        }
    }

    /// <summary>
    /// 記錄最新價格；與上次不同時通知該股票的所有觀察者。
    /// 個別觀察者丟出的例外不會中斷對其他觀察者的通知，全部通知完後以 <see cref="AggregateException"/> 一併拋出。
    /// </summary>
    public void UpdatePrice(string symbol, decimal newPrice)
    {
        IStockPriceObserver[] snapshot;
        decimal oldPrice;
        lock (_gate)
        {
            var hasOldPrice = _lastPrices.TryGetValue(symbol, out oldPrice);
            _lastPrices[symbol] = newPrice;

            if (!hasOldPrice || oldPrice == newPrice || !_observers.TryGetValue(symbol, out var observers))
                return;

            snapshot = observers.ToArray();
        }

        List<Exception>? errors = null;
        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnPriceChanged(symbol, newPrice, oldPrice);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException($"價格觀察者處理 {symbol} 的價格變動時發生例外。", errors);
    }
}
