namespace TWStockLib.Models
{
    /// <summary>
    /// 統一的查詢結果包裝（Result Pattern）：以 <see cref="IsSuccess"/> 區分成功/失敗，
    /// 失敗時帶 <see cref="ErrorCode"/> 與 <see cref="ErrorMessage"/>，呼叫端免到處 try-catch。
    /// </summary>
    public sealed class StockResult<T>
    {
        public bool IsSuccess { get; private init; }
        public T? Value { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        private StockResult() { }

        public static StockResult<T> Ok(T value)
            => new() { IsSuccess = true, Value = value };

        public static StockResult<T> Fail(string code, string message)
            => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };
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
    }
}
