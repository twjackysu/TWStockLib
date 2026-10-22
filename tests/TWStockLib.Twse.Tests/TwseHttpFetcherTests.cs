using System.Net;
using TWStockLib.Twse.Http;

namespace TWStockLib.Twse.Tests;

public class TwseHttpFetcherTests
{
    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task GetBig5StringAsync_DecodesBig5Bytes()
    {
        // 「台積電」的 Big5（CodePage 950）編碼，ISIN 股票清單頁即以此編碼回應
        byte[] big5 = [0xA5, 0x78, 0xBF, 0x6E, 0xB9, 0x71];
        var fetcher = new TwseHttpFetcher(new FakeHttpClientFactory(new FixedResponseHandler(big5)));

        var text = await fetcher.GetBig5StringAsync("https://isin.twse.com.tw/isin/C_public.jsp?strMode=2");

        Assert.Equal("台積電", text);
    }
}
