using System.Text;

namespace TWStockLib.Twse.Http;

/// <inheritdoc cref="IStockHttpFetcher" />
public sealed class TwseHttpFetcher : IStockHttpFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;

    static TwseHttpFetcher()
    {
        // 註冊編碼提供者，以支援 950 (繁體中文 Big5) 編碼
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TwseHttpFetcher(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        return await client.GetStringAsync(url, ct);
    }

    public async Task<string> GetBig5StringAsync(string url, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsByteArrayAsync(ct);
        return Encoding.GetEncoding(950).GetString(raw);
    }
}
