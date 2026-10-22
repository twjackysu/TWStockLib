using Microsoft.Extensions.Time.Testing;

namespace TWStockLib.Twse.Tests;

internal static class FakeTimeProviderExtensions
{
    /// <summary>以真實時間為上限的等候，避免實作有誤時測試卡住。</summary>
    public static readonly TimeSpan RealTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 每次推進 <paramref name="step"/> 並稍候讓背景的接續執行，直到 <paramref name="task"/> 完成，回傳總共推進的時間。
    /// 用在等候的計時器會在其他執行緒才建立、無法精確推進到期時間的情境；只適合斷言下限。
    /// </summary>
    public static async Task<TimeSpan> AdvanceUntilCompletedAsync(this FakeTimeProvider time, Task task, TimeSpan step)
    {
        var start = time.GetUtcNow();
        using var timeout = new CancellationTokenSource(RealTimeout);
        while (!task.IsCompleted)
        {
            time.Advance(step);
            await Task.Delay(1, timeout.Token);
        }

        await task;
        return time.GetUtcNow() - start;
    }
}
