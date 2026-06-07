using Microsoft.Extensions.Logging;
using TWStockLib.Cache;
using TWStockLib.Models;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources;

/// <summary>上櫃（OTC，櫃買中心 TPEX）資料來源。</summary>
public sealed class TpexStockDataSource : TwseFamilyDataSourceBase
{
    public TpexStockDataSource(
        IStockHttpFetcher fetcher, IStockParser parser, ICacheService cache,
        ILogger<TpexStockDataSource> logger)
        : base(fetcher, parser, cache, logger) { }

    public override MarketType Market => MarketType.OTC;
    protected override string MarketKeyPrefix => "otc";
    protected override int IsinStrMode => 4;

    protected override Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
        string symbol, DateTime month, CancellationToken ct)
        => CachedMonth(symbol, month, async () =>
        {
            var url = $"http://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock/st43_result.php?l=zh-tw&date={month:yyyy/MM/dd}&code={symbol}";
            var json = await Fetcher.GetStringAsync(url, ct);
            return Parser.ParseTpexHistory(json);
        });
}
