using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace TWStockLib.Twse.Http;

/// <summary>
/// 設定內建資料來源使用的具名 HttpClient（<see cref="TwStockOptions.HttpClientName"/>）。
/// 以 <c>TryAddEnumerable</c> 註冊這個類別，而不是每次 <c>AddHttpClient(name).ConfigureHttpClient(...)</c>，
/// 重複呼叫 <c>AddTwStock()</c> 也只會套用一次：User-Agent 不會重複加上，限流 handler 也不會掛兩層。
/// </summary>
internal sealed class TwStockHttpClientSetup(IOptions<TwStockOptions> twStockOptions)
    : IConfigureNamedOptions<HttpClientFactoryOptions>, IPostConfigureOptions<HttpClientFactoryOptions>
{
    /// <summary>
    /// 沿用 HttpClient 原本的 100 秒：ISIN 股票清單頁有數 MB，上游又傳得慢（實測上櫃清單 3.2 MB 約 24 秒），
    /// 而且逾時也包含在限流器排隊的時間，縮短反而容易逾時。
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    /// <summary>表明是哪個函式庫在呼叫，上游需要時能辨識與聯繫；版本取自組件版本。</summary>
    internal static readonly string UserAgent =
        $"TWStockLib/{typeof(TwStockHttpClientSetup).Assembly.GetName().Version!.ToString(3)} (+https://github.com/twjackysu/TWStockLib)";

    // 放在 Configure 階段：使用者在 AddTwStock 之後以 ConfigureHttpClient 設定的逾時與標頭會蓋過這裡的預設值
    public void Configure(string? name, HttpClientFactoryOptions options)
    {
        if (name != TwStockOptions.HttpClientName)
            return;

        options.HttpClientActions.Add(client =>
        {
            client.Timeout = DefaultTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
    }

    public void Configure(HttpClientFactoryOptions options) => Configure(Options.DefaultName, options);

    // 放在 PostConfigure 階段：排在使用者以 AddHttpMessageHandler / AddStandardResilienceHandler 加上的 handler 之後，
    // 也就是最內層，重試的每一次實際送出都會先取得限流許可
    public void PostConfigure(string? name, HttpClientFactoryOptions options)
    {
        if (name != TwStockOptions.HttpClientName || !twStockOptions.Value.RateLimit.Enabled)
            return;

        options.HttpMessageHandlerBuilderActions.Add(builder =>
            builder.AdditionalHandlers.Add(new RateLimitingHandler(builder.Services.GetRequiredService<PerHostRateLimiter>())));
    }
}
