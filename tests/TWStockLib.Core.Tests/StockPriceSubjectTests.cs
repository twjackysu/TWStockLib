namespace TWStockLib.Core.Tests;

public class StockPriceSubjectTests
{
    private sealed class RecordingObserver : IStockPriceObserver
    {
        public readonly List<(decimal NewPrice, decimal OldPrice)> Changes = [];
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
            => Changes.Add((newPrice, oldPrice));
    }

    [Fact]
    public void UpdatePrice_FirstUpdate_DoesNotNotify()
    {
        var subject = new StockPriceSubject();
        var observer = new RecordingObserver();
        subject.Subscribe("2330", observer);

        subject.UpdatePrice("2330", 100m);

        Assert.Empty(observer.Changes);
    }

    [Fact]
    public void UpdatePrice_PriceChanged_NotifiesWithOldAndNew()
    {
        var subject = new StockPriceSubject();
        var observer = new RecordingObserver();
        subject.Subscribe("2330", observer);

        subject.UpdatePrice("2330", 100m);
        subject.UpdatePrice("2330", 105m);

        var change = Assert.Single(observer.Changes);
        Assert.Equal(105m, change.NewPrice);
        Assert.Equal(100m, change.OldPrice);
    }

    [Fact]
    public void UpdatePrice_SamePrice_DoesNotNotify()
    {
        var subject = new StockPriceSubject();
        var observer = new RecordingObserver();
        subject.Subscribe("2330", observer);

        subject.UpdatePrice("2330", 100m);
        subject.UpdatePrice("2330", 100m);

        Assert.Empty(observer.Changes);
    }

    [Fact]
    public void Unsubscribe_StopsNotifications()
    {
        var subject = new StockPriceSubject();
        var observer = new RecordingObserver();
        subject.Subscribe("2330", observer);
        subject.UpdatePrice("2330", 100m);

        subject.Unsubscribe("2330", observer);
        subject.UpdatePrice("2330", 110m);

        Assert.Empty(observer.Changes);
    }

    private sealed class ThrowingObserver : IStockPriceObserver
    {
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
            => throw new InvalidOperationException("observer bug");
    }

    [Fact]
    public void UpdatePrice_AnObserverThrows_TheOthersAreStillNotified_ThenTheErrorIsRaised()
    {
        var subject = new StockPriceSubject();
        var healthy = new RecordingObserver();
        subject.Subscribe("2330", new ThrowingObserver());
        subject.Subscribe("2330", healthy);
        subject.UpdatePrice("2330", 100m);

        var error = Assert.Throws<AggregateException>(() => subject.UpdatePrice("2330", 105m));

        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
        Assert.Single(healthy.Changes);
    }

    private sealed class UnsubscribingObserver(StockPriceSubject subject) : IStockPriceObserver
    {
        public int Calls;
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
        {
            Calls++;
            subject.Unsubscribe(symbol, this);
        }
    }

    [Fact]
    public void UpdatePrice_AnObserverUnsubscribesInsideItsCallback_TheNotificationCompletes()
    {
        var subject = new StockPriceSubject();
        var once = new UnsubscribingObserver(subject);
        var healthy = new RecordingObserver();
        subject.Subscribe("2330", once);
        subject.Subscribe("2330", healthy);
        subject.UpdatePrice("2330", 100m);

        subject.UpdatePrice("2330", 105m);
        subject.UpdatePrice("2330", 110m);

        Assert.Equal(1, once.Calls);
        Assert.Equal(2, healthy.Changes.Count);
    }

    private sealed class CountingObserver : IStockPriceObserver
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice) => Interlocked.Increment(ref _calls);
    }

    [Fact]
    public void ConcurrentSubscriptions_AreAllKept()
    {
        var subject = new StockPriceSubject();
        var observers = Enumerable.Range(0, 2000).Select(_ => new CountingObserver()).ToArray();
        subject.UpdatePrice("2330", 100m);

        Parallel.ForEach(observers, observer => subject.Subscribe("2330", observer));
        subject.UpdatePrice("2330", 105m);

        Assert.All(observers, o => Assert.Equal(1, o.Calls));
    }

    [Fact]
    public void ConcurrentUpdatesWhileSubscribing_DoNotThrow()
    {
        var subject = new StockPriceSubject();

        var exception = Record.Exception(() => Parallel.For(0, 20_000, i =>
        {
            if (i % 2 == 0)
                subject.Subscribe("2330", new CountingObserver());
            else
                subject.UpdatePrice("2330", i % 4);
        }));

        Assert.Null(exception);
    }
}
