using System.Collections.Concurrent;
using System.Net;

namespace TWStockLib.Twse.Tests;

/// <summary>取代真正網路的最內層 handler：記錄收到的請求，即時報價回一筆 2330，其他端點回空的歷史資料。</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public const string TwseHistoryUrl = "https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date=20250901&stockNo=2330";
    public const string MisQuoteUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_2330.tw&json=1&delay=0";

    private const string QuoteJson =
        """{"msgArray":[{"c":"2330","n":"台積電","z":"600.00","y":"596.00","tlong":"1699000000000"}]}""";
    private const string EmptyHistoryJson = """{"stat":"OK","data":[]}""";

    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public IReadOnlyList<HttpRequestMessage> Requests => [.. _requests];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var body = request.RequestUri!.AbsolutePath.EndsWith("getStockInfo.jsp", StringComparison.Ordinal)
            ? QuoteJson
            : EmptyHistoryJson;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
