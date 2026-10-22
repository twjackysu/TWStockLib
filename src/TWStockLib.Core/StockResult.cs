using System.Diagnostics.CodeAnalysis;

namespace TWStockLib;

/// <summary>
/// 統一的查詢結果包裝（Result Pattern）：以 <see cref="IsSuccess"/> 區分成功/失敗，
/// 失敗時帶 <see cref="ErrorCode"/> 與 <see cref="ErrorMessage"/>，呼叫端免到處 try-catch。
/// 檢查過 <see cref="IsSuccess"/> 後，編譯器就知道 <see cref="Value"/>（或錯誤資訊）不是 <c>null</c>。
/// </summary>
public sealed class StockResult<T>
{
    private StockResult(bool isSuccess, T? value, string? errorCode, string? errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(ErrorCode), nameof(ErrorMessage))]
    public bool IsSuccess { get; }

    public T? Value { get; }

    /// <summary>失敗原因，見 <see cref="StockErrorCodes"/>。</summary>
    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    /// <summary>造成失敗的例外（若有），供記錄與診斷使用。</summary>
    public Exception? Exception { get; }

    public static StockResult<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, value, null, null, null);
    }

    public static StockResult<T> Fail(string code, string message, Exception? exception = null)
        => new(false, default, code, message, exception);

    /// <summary>成功時以 <paramref name="value"/> 取出結果。</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = Value;
        return IsSuccess;
    }

    /// <summary>成功時回傳結果；失敗時拋出帶錯誤碼的 <see cref="StockQueryException"/>。適合偏好例外流程的呼叫端。</summary>
    public T GetValueOrThrow()
        => IsSuccess ? Value : throw new StockQueryException(ErrorCode, ErrorMessage, Exception);

    public override string ToString()
        => IsSuccess ? $"Ok({Value})" : $"Fail({ErrorCode}: {ErrorMessage})";
}

/// <summary>由 <see cref="StockResult{T}.GetValueOrThrow"/> 拋出，帶失敗的錯誤碼。</summary>
public sealed class StockQueryException(string errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>失敗原因，見 <see cref="StockErrorCodes"/>。</summary>
    public string ErrorCode { get; } = errorCode;
}

/// <summary>常用錯誤碼。</summary>
public static class StockErrorCodes
{
    /// <summary>找不到對應市場的資料來源。</summary>
    public const string SourceNotFound = "SOURCE_NOT_FOUND";
    /// <summary>上游查無此股票 / 無資料。</summary>
    public const string NotFound = "NOT_FOUND";
    /// <summary>上游連線或回應錯誤。</summary>
    public const string UpstreamError = "UPSTREAM_ERROR";
    /// <summary>上游在 HttpClient 逾時內沒有回應。</summary>
    public const string Timeout = "TIMEOUT";
    /// <summary>上游回應的格式無法解析（例如被擋下時回傳的錯誤頁、或端點格式變動）。</summary>
    public const string ParseError = "PARSE_ERROR";
}
