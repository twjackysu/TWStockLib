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
}

public class StockResultTests
{
    [Fact]
    public void Ok_CarriesValue_AndIsSuccess()
    {
        var result = StockResult<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Fail_CarriesCodeAndMessage()
    {
        var result = StockResult<int>.Fail("CODE", "message");

        Assert.False(result.IsSuccess);
        Assert.Equal("CODE", result.ErrorCode);
        Assert.Equal("message", result.ErrorMessage);
    }
}
