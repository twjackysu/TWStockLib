using Microsoft.Extensions.Logging;
using TWStockLib.Cache;
using TWStockLib.Models;
using TWStockLib.Twse.Http;
using TWStockLib.Twse.Parsers;

namespace TWStockLib.Twse.DataSources
{
    /// <summary>上市（TSE，台灣證券交易所）資料來源。</summary>
    public sealed class TwseStockDataSource : TwseFamilyDataSourceBase
    {
        public TwseStockDataSource(
            IStockHttpFetcher fetcher, IStockParser parser, ICacheService cache,
            ILogger<TwseStockDataSource> logger)
            : base(fetcher, parser, cache, logger) { }

        public override MarketType Market => MarketType.TSE;
        protected override string MarketKeyPrefix => "tse";
        protected override int IsinStrMode => 2;

        protected override Task<IReadOnlyList<StockHistory>> FetchMonthAsync(
            string symbol, DateTime month, CancellationToken ct)
            => CachedMonth(symbol, month, async () =>
            {
                var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={month:yyyyMM01}&stockNo={symbol}";
                var json = await Fetcher.GetStringAsync(url, ct);
                return Parser.ParseTwseHistory(json);
            });
    }
}
