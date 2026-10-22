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

    /// <summary>上市個股日成交資訊：前一個交易日有行情的上市證券，不含權證（壓縮後約 50 KB）。</summary>
    protected override string OpenApiStockListUrl => "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL";

    /// <summary>上市權證基本資料彙總表（壓縮後約 1.8 MB、解壓後約 45 MB），含已到期的權證。</summary>
    protected override string OpenApiWarrantListUrl => "https://openapi.twse.com.tw/v1/opendata/t187ap37_L";

    protected override IReadOnlyDictionary<string, StockData> ParseOpenApiStockList(string json)
        => TwseStockParser.ParseTwseOpenApiStockList(json, Market);

    protected override IReadOnlyDictionary<string, StockData> ParseOpenApiWarrantList(string json)
        => TwseStockParser.ParseTwseOpenApiWarrantList(json, Market, TaiwanToday);

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
