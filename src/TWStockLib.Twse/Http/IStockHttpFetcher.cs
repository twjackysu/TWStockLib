namespace TWStockLib.Twse.Http
{
    /// <summary>封裝對證交所家族端點的 HTTP 取得，是唯一碰 <see cref="System.Net.Http.HttpClient"/> 的地方。</summary>
    public interface IStockHttpFetcher
    {
        /// <summary>以 UTF-8 取得回應字串。</summary>
        Task<string> GetStringAsync(string url, CancellationToken ct = default);

        /// <summary>以 Big5（CodePage 950）解碼取得回應字串，供 ISIN 股票清單頁使用。</summary>
        Task<string> GetBig5StringAsync(string url, CancellationToken ct = default);
    }
}
