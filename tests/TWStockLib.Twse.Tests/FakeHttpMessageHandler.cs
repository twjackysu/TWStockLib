using System.Collections.Concurrent;
using System.Net;

namespace TWStockLib.Twse.Tests;

/// <summary>取代真正網路的最內層 handler：記錄收到的請求，一律回傳一筆 2330 的即時報價。</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public const string QuoteJson =
        """{"msgArray":[{"c":"2330","n":"台積電","z":"600.00","y":"596.00","tlong":"1699000000000"}]}""";

    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public IReadOnlyList<HttpRequestMessage> Requests => [.. _requests];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(QuoteJson) });
    }
}
