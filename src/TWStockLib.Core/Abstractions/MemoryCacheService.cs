using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace TWStockLib.Abstractions;

/// <summary>
/// 以 <see cref="IMemoryCache"/> 實作的 <see cref="ICacheService"/>。<c>GetOrSetAsync</c> 會合併同一個 key
/// 同時發生的未命中（single-flight）：例如十個呼叫同時查詢尚未快取的股票清單，只會下載一次。
/// </summary>
public sealed class MemoryCacheService(IMemoryCache memoryCache) : ICacheService
{
    /// <summary>進行中的載入，值為 <see cref="Flight{T}"/>。載入結果寫入快取後才移除，之後的呼叫一定命中快取或開始新的載入。</summary>
    private readonly ConcurrentDictionary<string, object> _flights = new();

    public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value)
        => memoryCache.TryGetValue(key, out value) && value is not null;

    public async Task<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct = default)
    {
        while (true)
        {
            if (memoryCache.TryGetValue(key, out T? cachedValue) && cachedValue is not null)
                return cachedValue;
            ct.ThrowIfCancellationRequested();

            var flight = new Flight<T>();
            var existing = _flights.GetOrAdd(key, flight);
            if (ReferenceEquals(existing, flight))
            {
                _ = LoadAsync(key, flight, factory, expiry);
                return await flight.WaitAsync(ct);
            }

            // 同一個 key 被當成不同型別使用，無法共用結果，照一般的未命中處理
            if (existing is not Flight<T> running)
                return await RefreshAsync(key, factory, expiry, ct);

            if (running.TryJoin())
                return await running.WaitAsync(ct);

            // 那次載入的等候者剛好全部離開、正在取消：移除後重新開始
            _flights.TryRemove(KeyValuePair.Create(key, existing));
        }
    }

    public async Task<T> RefreshAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct = default)
    {
        var value = await factory(ct);
        if (value is not null)
            memoryCache.Set(key, value, expiry);

        return value;
    }

    private async Task LoadAsync<T>(string key, Flight<T> flight, Func<CancellationToken, Task<T>> factory, TimeSpan expiry)
    {
        try
        {
            var value = await factory(flight.Token);
            if (value is not null)
                memoryCache.Set(key, value, expiry);
            flight.Complete(value);
        }
        catch (Exception ex)
        {
            // 失敗不快取：這次的等候者都拿到同一個例外，下一個呼叫重新載入
            flight.Fail(ex);
        }
        finally
        {
            _flights.TryRemove(KeyValuePair.Create(key, (object)flight));
        }
    }

    /// <summary>
    /// 同一個 key 的一次載入，以及正在等它的呼叫端人數。每個等候者以自己的 token 等待，取消時只有它自己離開；
    /// 最後一位離開時若還沒載入完，就取消傳給 factory 的 token，沒有人要的結果不必再向上游要。
    /// </summary>
    private sealed class Flight<T>
    {
        private readonly TaskCompletionSource<T> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 沒有計時器、也不是 linked token，不需要 Dispose；不 Dispose 也免去與 Cancel 的競爭
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _gate = new();
        private int _waiters = 1; // 建立者本身
        private bool _abandoned;

        public CancellationToken Token => _cancellation.Token;

        /// <summary>加入等候；這次載入已被放棄（正在取消）時回傳 <c>false</c>。</summary>
        public bool TryJoin()
        {
            lock (_gate)
            {
                if (_abandoned)
                    return false;

                _waiters++;
                return true;
            }
        }

        public async Task<T> WaitAsync(CancellationToken ct)
        {
            try
            {
                return await _result.Task.WaitAsync(ct);
            }
            finally
            {
                Leave();
            }
        }

        public void Complete(T value) => _result.TrySetResult(value);

        public void Fail(Exception ex)
        {
            _result.TrySetException(ex);
            // 等候者全部離開後才失敗時沒有人會讀這個例外，先標記為已觀察，避免觸發 UnobservedTaskException
            _ = _result.Task.Exception;
        }

        private void Leave()
        {
            lock (_gate)
            {
                if (--_waiters > 0 || _result.Task.IsCompleted)
                    return;

                _abandoned = true;
            }

            _cancellation.Cancel();
        }
    }
}
