using System.Collections.Frozen;
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

    // 上次合併的結果，兩份清單都還是同一個快取實例時直接沿用；多執行緒同時換新頂多重算一次
    private MergedStockList? _withWarrants;

    private sealed class MergedStockList(
        IReadOnlyDictionary<string, StockData> listed, IReadOnlyDictionary<string, StockData> warrants)
    {
        public IReadOnlyDictionary<string, StockData> Listed { get; } = listed;
        public IReadOnlyDictionary<string, StockData> Warrants { get; } = warrants;
        public IReadOnlyDictionary<string, StockData> All { get; } = listed.Concat(warrants)
            .DistinctBy(p => p.Key)
            .ToFrozenDictionary();
    }

    /// <summary>
    /// 個股日成交資訊不含權證，只要權證以外的清單時只下載它（壓縮後約 50 KB）；含權證時另外取上市權證基本資料
    /// （壓縮後約 1.8 MB），兩者分開快取，各自只下載一次。
    /// </summary>
    protected override async Task<IReadOnlyDictionary<string, StockData>> FetchOpenApiStockListAsync(
        bool includeWarrant, CancellationToken ct)
    {
        var listed = await CachedStockList($"stock_list_openapi_{Market}", async token =>
        {
            var json = await Fetcher.GetStringAsync("https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL", token);
            return TwseStockParser.ParseTwseOpenApiStockList(json, Market);
        }, ct);
        if (!includeWarrant)
            return listed;

        var warrants = await CachedStockList($"stock_list_openapi_{Market}_warrants", async token =>
        {
            var json = await Fetcher.GetStringAsync("https://openapi.twse.com.tw/v1/opendata/t187ap37_L", token);
            return TwseStockParser.ParseTwseOpenApiWarrantList(json, Market, TaiwanToday);
        }, ct);

        var merged = _withWarrants;
        if (merged is null || !ReferenceEquals(merged.Listed, listed) || !ReferenceEquals(merged.Warrants, warrants))
            _withWarrants = merged = new MergedStockList(listed, warrants);
        return merged.All;
    }

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
