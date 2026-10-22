using System.Globalization;
using Microsoft.Extensions.Options;
using TWStockLib.Abstractions;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources;

/// <summary>上市（TSE，台灣證券交易所）資料來源。</summary>
internal sealed class TwseStockDataSource(
    IStockHttpFetcher fetcher, ICacheService cache, TimeProvider timeProvider, IOptions<TwStockOptions>? options = null)
    : TwseFamilyDataSourceBase(fetcher, cache, timeProvider, options)
{
    public override MarketType Market => MarketType.TSE;
    protected override string MarketKeyPrefix => "tse";
    protected override int IsinStrMode => 2;

    /// <summary>上市權證（含牛熊證）：6 碼，以 03～08 開頭，例如 030001、03989C。</summary>
    protected internal override bool IsWarrantSymbol(string symbol)
        => symbol.Length == 6 && symbol[0] == '0' && symbol[1] is >= '3' and <= '8';

    protected override Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateOnly month, CancellationToken ct)
        => CachedMonth(symbol, month, async token =>
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={month:yyyyMMdd}&stockNo={Uri.EscapeDataString(symbol)}");
            var json = await Fetcher.GetStringAsync(url, token);
            return TwseStockParser.ParseTwseHistory(json);
        }, ct);
}
