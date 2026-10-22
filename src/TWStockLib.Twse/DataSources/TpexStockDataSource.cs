using System.Globalization;
using Microsoft.Extensions.Options;
using TWStockLib.Abstractions;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources;

/// <summary>上櫃（OTC，櫃買中心 TPEX）資料來源。</summary>
internal sealed class TpexStockDataSource(
    IStockHttpFetcher fetcher, ICacheService cache, TimeProvider timeProvider, IOptions<TwStockOptions>? options = null)
    : TwseFamilyDataSourceBase(fetcher, cache, timeProvider, options)
{
    public override MarketType Market => MarketType.OTC;
    protected override string MarketKeyPrefix => "otc";
    protected override int IsinStrMode => 4;

    protected override Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateOnly month, CancellationToken ct)
        => CachedMonth(symbol, month, async token =>
        {
            // 「/」在自訂格式中是文化相依的日期分隔符號，必須用 InvariantCulture 才保證是斜線
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock/st43_result.php?l=zh-tw&date={month:yyyy/MM/dd}&code={symbol}");
            var json = await Fetcher.GetStringAsync(url, token);
            return TwseStockParser.ParseTpexHistory(json);
        }, ct);
}
