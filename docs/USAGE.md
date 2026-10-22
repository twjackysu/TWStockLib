# TWStockLib 使用說明

README 的快速入門之外，各項功能的行為細節、設定與擴充方式。

## 查詢結果與錯誤處理

查詢方法都回傳 `StockResult<T>`：以 `IsSuccess` 判斷成功，失敗時帶 `ErrorCode` / `ErrorMessage` / `Exception`。

```csharp
var result = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);
if (result.IsSuccess)
    Console.WriteLine(result.Value.LastPrice);          // 檢查過 IsSuccess，不必再寫 Value!
else
    Console.WriteLine($"{result.ErrorCode}: {result.ErrorMessage}");

if (result.TryGetValue(out var quote))                  // 或用 TryGetValue
    Console.WriteLine(quote.Name);

var value = result.GetValueOrThrow();                   // 偏好例外流程：失敗時拋出帶 ErrorCode 的 StockQueryException
```

- 呼叫端用 `CancellationToken` 取消時，照 .NET 慣例拋出 `OperationCanceledException`，不會變成失敗結果。
- 參數本身不合法（空白代號、起日晚於迄日）時拋出 `ArgumentException`。
  代號只接受 1 到 10 個英文字母或數字（例如 `2330`、`00631L`、指數 `t00`），區分大小寫；
  其他字元（例如 `2330.tw|tse_2317`）會改寫送給上游的查詢，一律拒絕。

## 各項查詢

### 股票清單

```csharp
var all = await stock.GetAllStockListAsync();
if (all.IsSuccess)
    Console.WriteLine($"共 {all.Value.Count} 支股票");

var tse = await stock.GetStockListAsync(MarketType.TSE);
var otcWithWarrants = await stock.GetStockListAsync(MarketType.OTC, StockListOptions.WithWarrants);

// 需要最完整的清單（含停止交易中、即將掛牌的證券）時改用 ISIN 頁面，比較慢
var complete = await stock.GetAllStockListAsync(new StockListOptions { Source = StockListSource.Isin });
```

每一檔有代號（`Symbol`）、名稱（`Name`）與市場別（`Market`）。預設排除權證，認購、認售與牛熊證都算在內；
是否為權證依代號判斷（上市 6 碼且 03～08 開頭、上櫃 6 碼且 7 開頭，2026/10/22 以 ISIN 的 CFICode 核對 53,863 檔完全一致）。

清單有兩種上游，以 `StockListOptions.Source` 選擇，沒指定時用 `OpenApi`：

| | `StockListSource.OpenApi`（預設） | `StockListSource.Isin` |
|---|---|---|
| 上游 | 證交所與櫃買 OpenAPI（JSON） | ISIN 證券編碼頁（Big5 網頁） |
| 速度（2026/10 實測） | 上市 0.2 秒、上櫃 0.1 秒；含權證約 1 秒 | 上市 5～78 秒、上櫃 2～24 秒，每個市場一頁數 MB |
| 涵蓋範圍 | 有行情資料的證券：權證以外 2,400 檔 | 最完整：權證以外 2,412 檔 |
| 少了什麼 | 停止交易中的證券、尚未掛牌的證券、資產基礎證券；上市清單收盤後才更新，新掛牌晚一個交易日出現；權證只列到最後交易日 | — |

2026/10/22 的差異是 12 檔：停止交易中的 1516 川飛、1589 永冠-KY、6142 友勁、4804 大略-KY、8047 星雲、00838B，
10/29 才掛牌的 009831、009832，以及 4 檔資產基礎證券（01111S～01114S）。只用來查代號名稱、判斷市場的話，預設就夠了。

每個上游的清單各自快取（預設 1 天），權證清單只在要求含權證時才下載；同時發生的多個查詢也只下載一次。

### 歷史資料

```csharp
var history = await stock.GetHistoricalDataAsync(
    "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

foreach (var d in history.GetValueOrThrow())
    Console.WriteLine($"{d.Date:yyyy-MM-dd} 開:{d.OpeningPrice} 收:{d.ClosingPrice} 漲跌:{d.Change}");
```

- 成交量 `TradeVolume` 一律是「股」、成交金額 `TurnoverValue` 一律是「元」，上市與上櫃一致。
- 當日無成交時開高低收為 `null`；除權息等不比價的日子 `Change` 為 `null`。

### 即時報價

```csharp
var result = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);
if (result.TryGetValue(out var q))
{
    Console.WriteLine($"{q.Symbol} {q.Name} 最新價:{q.LastPrice} 最高:{q.HighestPrice} 最低:{q.LowestPrice}");
    foreach (var ask in q.Asks)
        Console.WriteLine($"委賣 {ask.Price} × {ask.Volume} 張");
}
```

即時報價預設會在函式庫內快取 30 秒，同一檔股票在這段時間內重複查詢不會再打上游。
呼叫端自己已有節流、或需要「這一刻」的值時，可以略過快取：

```csharp
// 略過內部快取、一律向證交所查詢；查到的新報價仍會更新快取，之後的一般呼叫會拿到它
var fresh = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE, QuoteOptions.NoCache);
```

> 上游端點（mis.twse.com.tw）本身有頻率限制，略過快取後的呼叫頻率由呼叫端負責。
> 加權指數在 MIS 的代號是 `t00`，直接當作上市（`MarketType.TSE`）的一檔查即可；`LastPrice` 是指數、
> `YesterdayClosingPrice` 是昨收，指數沒有漲跌停，`LimitUp` / `LimitDown` 為 `null`。
> 盤前尚未有第一筆成交時 `LastPrice` 為 `null`。`SyncTime` 是台灣時間（`+08:00`）的 `DateTimeOffset`。
> 查無代號、或代號不屬於指定的市場（例如以 `MarketType.OTC` 查 2330）時回傳 `NOT_FOUND`。

### 一次查多檔

```csharp
string[] watchlist = ["2330", "2317", "6510"];
var quotes = await stock.GetRealtimeQuotesAsync(
    [("2330", MarketType.TSE), ("2317", MarketType.TSE), ("6510", MarketType.OTC)]);

foreach (var symbol in watchlist)
{
    var result = quotes[symbol];   // 每個傳入的代號恰好一筆
    Console.WriteLine(result.TryGetValue(out var q)
        ? $"{q.Symbol} {q.Name} {q.LastPrice}"
        : $"{symbol} {result.ErrorCode}: {result.ErrorMessage}");
}
```

- 同一市場的代號合併成一個請求（MIS 一次可查多檔，內建來源每 50 檔一個請求）；上市與上櫃各一個。
  逐檔呼叫 `GetRealtimeQuoteAsync` 查 10 檔要 10 個請求，受[限流](#限流)影響約需 15 秒；批次查詢只要 1～2 個。
- 回傳以代號為 key 的字典，**每一檔各自成功或失敗**：查無代號（或市場錯誤）為 `NOT_FOUND`、不支援的市場為
  `SOURCE_NOT_FOUND`；上游失敗時，同一市場這次查詢的每一檔都是同一個失敗結果。字典不保證順序，請依自己的清單取值。
- 快取以每一檔為單位，與單檔查詢共用：已快取的代號不會再查上游；`QuoteOptions.NoCache` 一律重查並更新快取。
- 重複的代號只查一次；同一代號指定兩個不同市場時拋出 `ArgumentException`。

### 不指定市場

不確定代號是上市還是上櫃時，可省略 `MarketType`，函式庫會依股票清單（預設的 OpenAPI 上游）判斷：

```csharp
var quote = await stock.GetRealtimeQuoteAsync("6510");                    // 上櫃
var quotes = await stock.GetRealtimeQuotesAsync(["2330", "6510", "0050"]);
var history = await stock.GetHistoricalDataAsync("2330", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));
await foreach (var q in stock.WatchAsync(["2330", "6510"], ct: stoppingToken)) { /* … */ }
```

- **第一次呼叫要下載股票清單**（OpenAPI，通常 1 秒內），之後快取 `StockListCacheDuration`（預設 1 天）。
  先查不含權證的清單、依上市、上櫃的順序找到就停，只查上市股票時不會下載上櫃清單；
  找不到的代號才再查含權證的清單（上市權證清單壓縮後約 1.8 MB）。
- 不在清單中的代號回傳 `NOT_FOUND`，包括打錯的代號、指數（`t00`），以及 OpenAPI 清單沒有的證券
  （停止交易中、當天才掛牌，見[股票清單](#股票清單)）；這些請改用指定市場的多載。
- 股票清單取得失敗時回傳該失敗（例如 `UPSTREAM_ERROR`），不會誤報成 `NOT_FOUND`；追蹤時則在下一輪重試。

### 追蹤成交價

```csharp
await foreach (var quote in stock.WatchAsync("2330", MarketType.TSE, ct: stoppingToken))
    Console.WriteLine($"{quote.SyncTime:HH:mm:ss} {quote.Symbol} {quote.LastPrice}");
```

- 先回傳一次目前的報價，之後每隔 `interval`（預設與最短皆為 5 秒）略過快取查詢，只在成交價變動時回傳。
- 上游暫時失敗會記錄後在下一輪重試；查無此代號時拋出 `StockQueryException`（`ErrorCode` 為 `NOT_FOUND`）。
- 以 `CancellationToken` 結束追蹤，結束時拋出 `OperationCanceledException`。

同時追蹤多檔請用多檔的多載，每一輪一次批次查詢，而不是每檔各開一個 `WatchAsync`：

```csharp
await foreach (var quote in stock.WatchAsync(
    [("2330", MarketType.TSE), ("2317", MarketType.TSE), ("6510", MarketType.OTC)], ct: stoppingToken))
{
    Console.WriteLine($"{quote.Symbol} {quote.LastPrice}");
}
```

- 每一檔第一次查到時回傳一次，之後只回傳成交價有變動的那幾檔（同一輪內依傳入的順序）。
- 每一輪每個市場一個請求（每 50 檔一個）：預設限流下，一輪 3 個請求以內（例如上市 100 檔 + 上櫃 50 檔）仍能維持 5 秒的間隔。
- 還沒查到過的代號回 `NOT_FOUND` 時拋出 `StockQueryException`（多半是代號或市場打錯）；已經查到過的代號之後查無資料、
  或某個市場暫時失敗，只記錄並在下一輪重試，不中斷其他代號。

## 設定

`AddTwStock` 可帶入設定，調整快取時間與限流，沒有設定的項目維持預設值：

```csharp
services.AddTwStock(options =>
{
    options.QuoteCacheDuration = TimeSpan.FromSeconds(10);
    options.ClosedMonthHistoryCacheDuration = TimeSpan.FromDays(7);
    options.RateLimit.PermitLimit = 2;
    options.RateLimit.Window = TimeSpan.FromSeconds(5);
});
```

| 設定 | 預設 | 說明 |
|------|------|------|
| `QuoteCacheDuration` | 30 秒 | 即時報價的快取時間；單次略過快取用 `QuoteOptions.NoCache` |
| `StockListCacheDuration` | 1 天 | 股票清單的快取時間 |
| `ClosedMonthHistoryCacheDuration` | 1 天 | 已結束月份（台灣時間）的歷史資料，不再變動 |
| `CurrentMonthHistoryCacheDuration` | 30 分鐘 | 當月與未來月份的歷史資料，每個交易日收盤後會多一列 |
| `RateLimit.Enabled` | `true` | 是否啟用內建限流 |
| `RateLimit.PermitLimit` | 3 | 每個主機在 `Window` 內最多送出的請求數 |
| `RateLimit.Window` | 5 秒 | 限流的滑動視窗長度 |

快取時間與 `Window` 必須大於 0、`PermitLimit` 至少為 1，否則第一次取得服務時拋出 `OptionsValidationException`。
也可以從設定檔繫結：`services.Configure<TwStockOptions>(configuration.GetSection("TwStock"))`。

### 限流

證交所會暫時封鎖短時間內送出大量請求的 IP。官方沒有公告數字，社群經驗是**每 5 秒超過 3 個請求**就可能被擋，
因此內建限流預設採用這個頻率，並對每個上游主機（`www.twse.com.tw`、`mis.twse.com.tw`、`openapi.twse.com.tw`、
`www.tpex.org.tw`、`isin.twse.com.tw`）分開計算：任何 5 秒內最多送出 3 個請求，超過的請求排隊等候，不會失敗。

- 歷史資料一個月一個請求：查一整年（12 個月）約需 15 秒。查歷史不會拖慢即時報價，兩者是不同主機。
- 逐檔查報價、或每檔各開一個 `WatchAsync` 時，超過 3 檔就會排隊（例如 6 檔約每 10 秒一次）。
  多檔請改用 `GetRealtimeQuotesAsync` 與多檔的 `WatchAsync`，同一市場每 50 檔只需一個請求。
- 排隊的時間也算在 HttpClient 的逾時內；呼叫端可用 `CancellationToken` 放棄等候。
- 限流在 HttpClient 管線的最內層，自行加上的重試每一次實際送出也都會計入。
- 同一個應用程式內共用額度；多個行程或多台機器共用同一個對外 IP 時，額度不會合併計算，請自行調低 `PermitLimit`。

### HttpClient 與重試

內建來源使用具名 HttpClient `TwStockOptions.HttpClientName`（`"TWStockLib"`），預設逾時 100 秒、
User-Agent 為 `TWStockLib/{版本}`。以同一個名稱取得 `IHttpClientBuilder`，就能加上重試、調整逾時或設定 Proxy：

```csharp
// dotnet add package Microsoft.Extensions.Http.Resilience
services.AddTwStock();
services.AddHttpClient(TwStockOptions.HttpClientName)
    .AddStandardResilienceHandler(o =>
    {
        // 改用 ISIN 清單（StockListSource.Isin）時，清單頁有數 MB、上游傳得慢（實測上市可達 78 秒），
        // 排隊等限流的時間也算在內，標準預設（單次 10 秒、總計 30 秒）太短
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(180);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);   // 須至少為單次逾時的兩倍
    });
```

只想調整逾時：

```csharp
services.AddHttpClient(TwStockOptions.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(60));
```

請在 `AddTwStock()` **之後**設定，才會蓋過內建的預設值。

## 擴充

- **自訂資料來源**：實作 `TWStockLib.Abstractions.IStockDataSource` 並註冊；同一市場註冊多個來源時以最後註冊的為準，
  因此可以用來替換內建來源（例如改接券商 API）。

  ```csharp
  services.AddTwStock();
  services.AddSingleton<IStockDataSource, MyBrokerTseSource>();   // Market => MarketType.TSE，取代內建的上市來源
  ```

  批次報價用的 `FetchRealtimeQuotesAsync` 有預設實作（逐檔呼叫 `FetchRealtimeQuoteAsync`），不必實作；
  上游支援一次查多檔時再覆寫，以減少請求數。`FetchStockListAsync(StockListOptions)` 要處理 `IncludeWarrant`；
  只有一個清單上游的來源可以不理會 `Source`。

- **替換快取**：在 `AddTwStock()` 之前註冊自己的 `ICacheService`，會保留你的實作。介面有三個方法：
  `GetOrSetAsync`（有就用，沒有就載入並快取）、`RefreshAsync`（一律重新載入並覆寫）、`TryGetValue`（只查不載入）；
  `null` 代表查無資料，不要快取。內建的 `MemoryCacheService` 會把同一個 key 同時發生的未命中合併成一次上游請求，
  自訂快取若要同樣的效果再自行實作。

## 錯誤碼

`StockResult<T>.ErrorCode` 可能的值（見 `StockErrorCodes`）：

| 錯誤碼 | 意義 |
|--------|------|
| `SOURCE_NOT_FOUND` | 找不到對應市場的資料來源 |
| `NOT_FOUND` | 上游查無此股票 / 無資料 |
| `UPSTREAM_ERROR` | 上游連線或回應錯誤，包括 MIS 拒絕查詢（`rtcode` 不是 `0000`） |
| `TIMEOUT` | 上游在 HttpClient 逾時內沒有回應（2.3.0 起） |
| `PARSE_ERROR` | 上游回應的格式無法解析，例如被擋下時回傳的錯誤頁（3.0.0 起） |

## 注意事項

1. 所有資料皆從網路即時獲取，需要連線。
2. 歷史數據以月為單位查詢，跨月會自動合併。
3. 即時報價快取 30 秒、股票清單與已結束月份的歷史快取 1 天、當月歷史快取 30 分鐘，以減少上游請求，可由 `TwStockOptions` 調整（見[設定](#設定)）。即時報價可用 `QuoteOptions.NoCache` 略過快取。
4. 歷史資料的成交量一律是「股」、成交金額一律是「元」，上市與上櫃單位一致。
5. 對每個上游主機預設每 5 秒最多 3 個請求，超過的請求排隊等候（見[限流](#限流)）。
