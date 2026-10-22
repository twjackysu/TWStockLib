using Microsoft.Extensions.Options;

namespace TWStockLib;

/// <summary>在第一次取用設定時檢查 <see cref="TwStockOptions"/>，避免不合法的值到了快取或限流時才出錯。</summary>
internal sealed class TwStockOptionsValidator : IValidateOptions<TwStockOptions>
{
    public ValidateOptionsResult Validate(string? name, TwStockOptions options)
    {
        List<string> failures = [];

        // MemoryCache 不接受 0 或負的到期時間；不要快取請改用 QuoteOptions.NoCache
        RequirePositive(options.QuoteCacheDuration, nameof(TwStockOptions.QuoteCacheDuration));
        RequirePositive(options.StockListCacheDuration, nameof(TwStockOptions.StockListCacheDuration));
        RequirePositive(options.ClosedMonthHistoryCacheDuration, nameof(TwStockOptions.ClosedMonthHistoryCacheDuration));
        RequirePositive(options.CurrentMonthHistoryCacheDuration, nameof(TwStockOptions.CurrentMonthHistoryCacheDuration));

        if (options.RateLimit.PermitLimit < 1)
            failures.Add($"RateLimit.PermitLimit 必須至少為 1（目前為 {options.RateLimit.PermitLimit}）；不要限流請將 RateLimit.Enabled 設為 false。");
        RequirePositive(options.RateLimit.Window, "RateLimit.Window");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void RequirePositive(TimeSpan value, string propertyName)
        {
            if (value <= TimeSpan.Zero)
                failures.Add($"{propertyName} 必須大於 0（目前為 {value}）。");
        }
    }
}
