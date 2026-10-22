using System.Globalization;

namespace TWStockLib.Core.Tests;

public class StockHistoryTests
{
#pragma warning disable CS0618 // 驗證已標記 Obsolete、但 2.x 仍保留的舊建構式
    [Fact]
    public void LegacyConstructor_ParsesNumbersTheSameWayInAnyCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // 「.」是千分位、「,」是小數點
        try
        {
            var history = new StockHistory(
                ["108/11/01", "1,000,000", "30,000,000", "30.00", "31.00", "29.50", "30.50", "+0.50", "500"]);

            Assert.Equal(new DateTime(2019, 11, 1), history.Date);
            Assert.Equal(1_000_000u, history.TradeVolume);
            Assert.Equal(30_000_000m, history.TurnOverInValue);
            Assert.Equal(30.50m, history.ClosingPrice);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
#pragma warning restore CS0618
}
