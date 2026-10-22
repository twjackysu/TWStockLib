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

    /// <summary>上櫃權證：6 碼，以 7 開頭，例如 700019、72479U。</summary>
    protected internal override bool IsWarrantSymbol(string symbol) => symbol.Length == 6 && symbol[0] == '7';

    /// <summary>
    /// 上櫃股票收盤行情：不含權證（約 360 KB）。另有含權證的「上櫃股票行情」（約 5 MB），
    /// 2026/10/22 晚間實測傳到一半常被上游中斷，不用它。
    /// </summary>
    protected override string OpenApiStockListUrl => "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";

    /// <summary>上櫃權證收盤行情日報表（約 3.8 MB）。</summary>
    protected override string OpenApiWarrantListUrl => "https://www.tpex.org.tw/openapi/v1/tpex_warrant_daily_quts";

    protected override IReadOnlyDictionary<string, StockData> ParseOpenApiStockList(string json)
        => TwseStockParser.ParseTpexOpenApiStockList(json, Market);

    protected override IReadOnlyDictionary<string, StockData> ParseOpenApiWarrantList(string json)
        => TwseStockParser.ParseTpexOpenApiWarrantList(json, Market);

    protected override Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateOnly month, CancellationToken ct)
        => CachedMonth(symbol, month, async token =>
        {
            // 「/」在自訂格式中是文化相依的日期分隔符號，必須用 InvariantCulture 才保證是斜線
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock/st43_result.php?l=zh-tw&date={month:yyyy/MM/dd}&code={Uri.EscapeDataString(symbol)}");
            var json = await Fetcher.GetStringAsync(url, token);
            return TwseStockParser.ParseTpexHistory(json);
        }, ct);
}
