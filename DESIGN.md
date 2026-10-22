# TWStockLib — 設計文件

> 對應版本：3.1
> 目標：**職責清楚、可測試、易擴充、可多框架發佈到 NuGet** 的類別庫。
> 設計心法承襲 `DataAnalysisLibrary`，但**刻意不過度設計**——只引入對本 lib 真正有價值的抽象。

---

## 目錄

1. [設計目標與原則](#1-設計目標與原則)
2. [核心心法（從 DataAnalysisLibrary 借什麼、不借什麼）](#2-核心心法)
3. [整體架構分層](#3-整體架構分層)
4. [方案結構（Solution Structure）](#4-方案結構)
5. [公開 API 與擴充點](#5-公開-api-與擴充點)
6. [Result 模型（StockResult&lt;T&gt;）](#6-result-模型)
7. [每市場一個 DataSource（OCP 擴充）](#7-每市場一個-datasource)
8. [Parser / Fetcher 分離（可測試性）](#8-parser--fetcher-分離)
9. [不可變模型與快取](#9-不可變模型與快取)
10. [成交價追蹤（WatchAsync）](#10-成交價追蹤watchasync)
11. [設計模式總表](#11-設計模式總表)
12. [演進紀錄](#12-演進紀錄)
13. [Mechanism vs Policy — Library 與應用層的邊界](#13-mechanism-vs-policy)
14. [刻意不做的事（避免過度設計）](#14-刻意不做的事)

---

## 1. 設計目標與原則

| 目標 | 說明 |
|------|------|
| **關注點分離** | 抓資料（IO）、解析（純字串→Model）、編排（Service）各自獨立 |
| **低耦合、高內聚** | 新增或替換一個市場的資料來源不需修改現有程式碼（OCP） |
| **可測試性** | 解析邏輯不依賴網路，可餵固定 HTML/JSON fixture 單元測試 |
| **全非同步 + 可取消** | 所有對外 IO 以 `async/await` 實作，並貫穿 `CancellationToken` |
| **型別安全 + 可空標註** | `Nullable=enable`，回傳一致以 `StockResult<T>` 包裝，並標註 `MemberNotNullWhen` |
| **最小公開面** | 只公開使用者需要的型別與擴充點；實作細節一律 `internal`，每個公開型別都是 SemVer 承諾 |
| **可多框架發佈** | `net8.0;net9.0;net10.0`，標準 NuGet metadata + SourceLink + symbols |

---

## 2. 核心心法

DataAnalysisLibrary 的精神只有兩條，套到本 lib：

1. **純邏輯與 IO 分離**
   DataAnalysisLibrary：`IQueryBuilder`（純字串、可測） ↔ `IQueryExecutor`（碰 SDK / IO）。
   TWStockLib 對應：`TwseStockParser`（HTML/JSON 字串 → Model，純函式） ↔ `IStockHttpFetcher`（只負責抓網頁）。

2. **每個來源一套實作、集合/字典註冊、OCP 擴充**
   DataAnalysisLibrary：`IDataSourceFactory` 用 `SourceKey` 路由，`AddSource()` 一行新增。
   TWStockLib 對應：**每個市場一個 `IStockDataSource`**，以 `MarketType` 為 key 註冊進字典，`StockMarketService` 只認介面、不寫 `if (TSE) … else (OTC)`。

**不借**的部分（對本 lib 屬過度設計）：Bridge、QueryOrchestrator、泛型 Template-Method Strategy。本 lib 查詢維度固定（報價／歷史／清單），來源同屬證交所家族，這些抽象帶來的成本大於收益。

---

## 3. 整體架構分層

```
┌──────────────────────────────────────────────┐
│            呼叫端（Console / Web API）          │
└───────────────────────┬──────────────────────┘
                        │ 只依賴 IStockMarketService（TWStockLib）
┌───────────────────────▼──────────────────────┐
│   StockMarketService  (Facade / 編排)          │
│   GetRealtimeQuoteAsync / GetHistoricalAsync   │
│   GetStockListAsync / WatchAsync               │
│   回傳 StockResult<T>                           │
└───────────────────────┬──────────────────────┘
            │ 依 MarketType 取得
┌───────────▼──────────────────────────────────┐
│   IReadOnlyDictionary<MarketType,             │
│                       IStockDataSource>        │  ← 唯一的公開擴充點
├───────────────┬───────────────────────────────┤
│ TwseStockData │ TpexStockDataSource │ 自訂來源 │  ← 內建來源為 internal
│ Source (TSE)  │ (OTC)               │  …       │
└───────────────┴───────────────────────────────┘
        │ 每個內建 DataSource 內部再分兩刀：
   ┌────┴─────────┐
┌──▼────────────┐ ┌▼─────────────────────────────┐
│TwseStockParser│ │ IStockHttpFetcher            │
│(純函式解析)    │ │ (抓網頁 + Big5 編碼，碰 IO)   │
└───────────────┘ └──────────────────────────────┘
```

---

## 4. 方案結構

```
TWStockLib.slnx
├── Directory.Build.props / .targets      ← 共用 metadata / Nullable / LangVersion / SourceLink
├── README.md / DESIGN.md
├── src/
│   ├── TWStockLib.Core/                   ← 無第三方 SDK 依賴（只靠 BCL + Microsoft.Extensions）
│   │   ├── IStockMarketService.cs / StockMarketService.cs / StockMarketServiceExtensions.cs
│   │   ├── StockQuote.cs / StockHistory.cs / StockData.cs / PriceLevel.cs / MarketType.cs / SecurityType.cs
│   │   ├── QuoteOptions.cs
│   │   ├── StockResult.cs                 ← Result Pattern + StockErrorCodes + StockQueryException
│   │   └── Abstractions/                  ← 擴充點
│   │       ├── IStockDataSource.cs
│   │       ├── ICacheService.cs
│   │       └── MemoryCacheService.cs
│   └── TWStockLib.Twse/                    ← 證交所家族實作（全部 internal），依賴 AngleSharp
│       ├── DataSources/  TwseFamilyDataSourceBase / TwseStockDataSource (TSE) / TpexStockDataSource (OTC)
│       ├── Parsers/      TwseStockParser（static，純函式）
│       ├── Http/         IStockHttpFetcher / TwseHttpFetcher、TwStockHttpClientSetup（具名 HttpClient）、
│       │                 RateLimitingHandler / PerHostRateLimiter / SlidingLogRateLimiter（限流）
│       ├── TwStockOptions.cs                      (公開設定：快取時間、限流、HttpClientName)
│       └── TwStockServiceCollectionExtensions.cs  (AddTwStock，命名空間 Microsoft.Extensions.DependencyInjection)
└── tests/
    ├── TWStockLib.Core.Tests/             ← Service 路由、錯誤收斂、WatchAsync（FakeTimeProvider）、StockResult、快取
    └── TWStockLib.Twse.Tests/             ← Parser 餵 fixture 字串（離線）、DataSource 快取行為、DI 註冊、
                                             限流與具名 HttpClient（假 HttpMessageHandler + FakeTimeProvider）
```

> **分包理由**：`Core` 沒有第三方 SDK 依賴，單元測試最乾淨；想換解析來源（例如改接券商 API）只要新增一個 connector 套件，不動 Core。

---

## 5. 公開 API 與擴充點

```csharp
namespace TWStockLib;

public interface IStockMarketService
{
    Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, QuoteOptions? options = null, CancellationToken ct = default);

    Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, MarketType market, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default);

    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);

    IAsyncEnumerable<StockQuote> WatchAsync(
        string symbol, MarketType market, TimeSpan? interval = null, CancellationToken ct = default);
}
```

- 只傳 `CancellationToken` 的報價查詢由 `StockMarketServiceExtensions` 提供，介面實作者只需實作一個方法。
- 依證券類型取清單（3.1 起）同樣是擴充方法：`GetStockListAsync(market, IReadOnlyCollection<SecurityType>)` 與
  `GetAllStockListAsync(IReadOnlyCollection<SecurityType>)`。它們呼叫既有的 `includeWarrant` 多載（要權證時取完整清單）
  再依 `StockData.SecurityType` 過濾，介面不必新增成員；以集合運算式 `[SecurityType.Etf]` 呼叫時不會與 `bool` 多載混淆。
- 選項用 `QuoteOptions`（`Default` / `NoCache`）而非 `bool` 旗標參數：新增選項不必再加多載。

3.1 新增（只加不改，既有成員的簽章不變）：

```csharp
// 批次報價與多檔追蹤：同一市場合併查詢
Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
    IEnumerable<(string Symbol, MarketType Market)> symbols, QuoteOptions? options = null, CancellationToken ct = default);
IAsyncEnumerable<StockQuote> WatchAsync(
    IEnumerable<(string Symbol, MarketType Market)> symbols, TimeSpan? interval = null, CancellationToken ct = default);

// 不指定市場：依股票清單判斷上市或上櫃
Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(string symbol, QuoteOptions? options = null, CancellationToken ct = default);
Task<IReadOnlyDictionary<string, StockResult<StockQuote>>> GetRealtimeQuotesAsync(
    IEnumerable<string> symbols, QuoteOptions? options = null, CancellationToken ct = default);
Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
    string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);
IAsyncEnumerable<StockQuote> WatchAsync(string symbol, TimeSpan? interval = null, CancellationToken ct = default);
IAsyncEnumerable<StockQuote> WatchAsync(IEnumerable<string> symbols, TimeSpan? interval = null, CancellationToken ct = default);
```

設計取捨見 [10. 成交價追蹤](#10-成交價追蹤watchasync) 與 [10.1 批次報價與自動判斷市場](#101-批次報價與自動判斷市場)。

擴充點集中在 `TWStockLib.Abstractions`：

```csharp
public interface IStockDataSource
{
    MarketType Market { get; }
    Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, QuoteOptions options, CancellationToken ct = default);
    // 3.1：預設實作逐檔呼叫 FetchRealtimeQuoteAsync，既有的第三方來源不必修改；內建來源覆寫為每 50 檔一個 MIS 請求
    Task<IReadOnlyDictionary<string, StockQuote>> FetchRealtimeQuotesAsync(
        IReadOnlyList<string> symbols, QuoteOptions options, CancellationToken ct = default) { /* 逐檔 */ }
    Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(bool includeWarrant = false, CancellationToken ct = default);
}

public interface ICacheService
{
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry);
    Task<T> RefreshAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry);   // 一律重新取得並覆寫

    // 3.1 起：factory 收到共用載入的 token，實作可合併同一 key 的並行未命中；預設實作直接呼叫上面的多載
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiry, CancellationToken ct)
        => GetOrSetAsync(key, () => factory(ct), expiry);
}
```

`ICacheService` 以兩個意圖明確的方法取代「`bypassCache` 布林參數」：呼叫端一看就知道是「有就用」還是「強制更新」。

`IStockDataSource.FetchStockListAsync` 的簽章在 3.1 不變：`includeWarrant: true` 本來就是「完整清單」，
依類型過濾只需要來源填好 `StockData.SecurityType`。改成新方法或新參數只會讓第三方來源多一種寫法，換不到能力。

設定與 HTTP 管線的擴充點（3.1 起，位於 `TWStockLib` 套件）：

```csharp
services.AddTwStock(options => { ... });                    // TwStockOptions：快取時間、RateLimit
services.AddHttpClient(TwStockOptions.HttpClientName)        // 同一個具名 HttpClient 的 IHttpClientBuilder
    .AddStandardResilienceHandler();                         // 重試等韌性由應用層自行決定要不要加
```

- `AddTwStock()` 保留原本的無參數多載，另加 `AddTwStock(Action<TwStockOptions>)`，不改變既有方法簽章（二進位相容）。
- 只公開設定與 client 名稱；限流器、handler 都是 internal，換實作不影響使用者。

---

## 6. Result 模型

```csharp
public sealed class StockResult<T>
{
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(ErrorCode), nameof(ErrorMessage))]
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }          // 保留原因，供記錄與診斷

    public bool TryGetValue(out T value);
    public T GetValueOrThrow();                    // 失敗時拋出帶 ErrorCode 的 StockQueryException
}
```

錯誤碼（`StockErrorCodes`）：`SOURCE_NOT_FOUND`（不支援的市場）、`NOT_FOUND`（查無此股）、`UPSTREAM_ERROR`（連線 / 回應錯誤）、
`TIMEOUT`（HttpClient 逾時）、`PARSE_ERROR`（回應格式無法解析）。

哪些情況**不**收斂成 Result：

- **呼叫端取消**：照 .NET 慣例拋出 `OperationCanceledException`。
- **呼叫端程式錯誤**（空白或格式不合法的代號、起日晚於迄日、過短的追蹤間隔、同一代號指定兩個市場）：在呼叫當下拋出 `ArgumentException`。

代號格式（3.1 起）：只接受 1 到 10 個 ASCII 英文字母或數字。代號會組進上游 URL，MIS 又以「|」分隔多檔，
放行任意字元等於讓輸入改寫查詢（`2330.tw|tse_2317` 會多查一檔）。資料來源另以 `Uri.EscapeDataString` 跳脫代號，
讓「&」「#」改不了其他查詢參數；但 MIS 會先解碼再分隔，實測 `%7C` 一樣被當成分隔符號，所以真正擋下「|」的是格式檢查。

> 採用理由：對齊 DataAnalysisLibrary 的 `QueryResult<T>`，呼叫端不用在每次呼叫包 try-catch，網路與格式錯誤都收斂成一致回傳；
> 但取消與參數錯誤屬於呼叫端自己的控制流程，維持 .NET 的例外慣例。

---

## 7. 每市場一個 DataSource

每個市場各自一個 `IStockDataSource`。證交所與櫃買的報價、清單流程相同，只差代碼前綴、ISIN strMode 與歷史端點，
由 `TwseFamilyDataSourceBase` 以抽象成員提供（Template Method），子類別只關心自己的端點與解析。

註冊（DI）：

```csharp
public static IServiceCollection AddTwStock(this IServiceCollection services)
{
    services.AddHttpClient();
    services.AddMemoryCache();
    services.TryAddSingleton(TimeProvider.System);
    services.TryAddSingleton<ICacheService, MemoryCacheService>();

    services.AddOptions<TwStockOptions>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TwStockOptions>, TwStockOptionsValidator>());

    // 具名 HttpClient：User-Agent / 逾時（Configure）與限流 handler（PostConfigure，最內層）
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HttpClientFactoryOptions>, TwStockHttpClientSetup>());
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<HttpClientFactoryOptions>, TwStockHttpClientSetup>());
    services.TryAddSingleton(sp => new PerHostRateLimiter(/* RateLimit 設定 */, sp.GetRequiredService<TimeProvider>()));
    services.TryAddSingleton<IStockHttpFetcher, TwseHttpFetcher>();

    services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TwseStockDataSource>());
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IStockDataSource, TpexStockDataSource>());   // ← 新增市場只多這一行

    services.TryAddSingleton<IStockMarketService, StockMarketService>();
    return services;
}
```

- 全部用 `TryAdd*`：可重複呼叫；使用者在之前註冊的相依（例如自己的 `ICacheService`）會被保留。
  具名 HttpClient 的設定也以 `TryAddEnumerable` 註冊的 options 類別套用，重複呼叫不會重複加 User-Agent 或多掛一層限流。
- `StockMarketService` 把 `IEnumerable<IStockDataSource>` 收成 `Dictionary<MarketType, …>`，同一市場以**最後註冊**的為準，
  因此使用者可以在 `AddTwStock()` 之後註冊自己的來源來取代內建來源。
- 服務是 Singleton 且無狀態，可注入任何生命週期的元件。

---

## 8. Parser / Fetcher 分離

- `IStockHttpFetcher` → 唯一碰 `HttpClient` 的地方，含 `Encoding.RegisterProvider` 與 Big5（CodePage 950）處理；測試以假實作替換。
- `TwseStockParser` → `internal static`，純函式，吃字串吐 Model。只有一個實作、方法又綁定 Twse / Tpex 端點，
  因此不另外抽介面（3.0 移除了原本的 `IStockParser`）。
- 解析一律使用 `InvariantCulture`；上櫃的「仟股 / 仟元」在解析時換算成與上市一致的「股 / 元」；
  格式不符的列直接略過，不以預設值捏造資料。
- 測試把證交所 / 櫃買的真實回應存成 fixture 字串，餵進 Parser 斷言輸出，**不需要網路、穩定、快**。
- MIS 報價（3.1 起）：`ParseRealtimeQuotes` 解析 `msgArray` 的每一筆，略過代號（`c`）為空的佔位資料
  （查無代號、市場錯誤都是這筆）；`rtcode` 不是 `0000` 時拋出 `HttpRequestException`（`UPSTREAM_ERROR`），
  例如一次查太多檔時的 9999「參數不足」——此時沒有 `msgArray`，不能當成每一檔都查無資料。

### HttpClient 管線與限流（3.1 起）

`TwseHttpFetcher` 以具名 client（`TwStockOptions.HttpClientName`）送出請求，管線由外而內：

```
使用者加的 handler（例如 AddStandardResilienceHandler 的重試）
  → RateLimitingHandler（最內層，每一次實際送出都先取得許可）
    → primary handler（SocketsHttpHandler）
```

- **為什麼要內建限流**：證交所會暫時封鎖短時間大量請求的 IP。歷史資料一個月一個請求、依序抓取，長區間查詢會連續打上游；
  多檔 `WatchAsync` 各自每 5 秒查一次。官方沒有公告門檻，社群經驗（twstock 等）是每 5 秒 3 個請求，預設採用這個值。
- **每主機分開計算**：`PerHostRateLimiter`（`PartitionedRateLimiter<HttpRequestMessage>`）依 `RequestUri.Host` 分區，
  查歷史（www.twse.com.tw）不會拖慢即時報價（mis.twse.com.tw）。
- **嚴格滑動視窗**：`SlidingLogRateLimiter` 繼承 `System.Threading.RateLimiting.RateLimiter`，記下每個許可的核發時間，
  任何 5 秒內最多 3 個。不用內建的 `SlidingWindowRateLimiter` / `TokenBucketRateLimiter`：它們以計時器分段補充，
  分段邊界前後可放行接近兩倍；且直接讀 `Stopwatch`，無法以 `FakeTimeProvider` 測試。等候走 `TimeProvider`，可取消。
- **排隊而不失敗**：拿不到許可就等，不回錯誤；等候時間算在 HttpClient 逾時內，呼叫端可用 `CancellationToken` 放棄。
- **放在最內層**：限流 handler 以 `IPostConfigureOptions<HttpClientFactoryOptions>` 加入，排在使用者所有 `AddHttpMessageHandler`
  之後，重試不會繞過限流。限流狀態是 Singleton，不隨 `IHttpClientFactory` 輪替 handler 而歸零。
- **逾時與 User-Agent**：逾時明確設為 100 秒（與 HttpClient 原本預設相同）：ISIN 股票清單頁有數 MB、上游傳得慢
  （實測上櫃清單 3.2 MB 約 24 秒），而且排隊時間也算在內。User-Agent 為 `TWStockLib/{版本} (+專案網址)`，讓上游能辨識來源。
- **不內建重試**：重試策略（次數、退避、斷路器）因部署環境而異，交給使用者以同一個 client 名稱自行加上，函式庫不相依 Polly。

---

## 9. 不可變模型與快取

- `StockQuote`、`StockHistory`、`StockData` 是 `sealed record`（init-only）；五檔報價是 `IReadOnlyList<PriceLevel>`。
- Parser 回傳唯讀集合（`ReadOnlyCollection`、`FrozenDictionary`），同一個實例可以安全地放進快取、交給多個呼叫端，
  不必像 2.3 那樣每次回傳都做複本。
- 自訂 `IStockDataSource` 的實作者也應回傳不可變集合（介面文件已註明）。
- 快取時間：即時報價 30 秒；股票清單與已結束月份的歷史 1 天；當月（以台灣時間判斷）與未來月份的歷史 30 分鐘。
  3.1 起可由 `TwStockOptions` 覆寫，預設值不變；不合法的值（0 或負）在第一次取得服務時以 `OptionsValidationException` 回報。

### 股票清單：一次下載、讀取時過濾（3.1 起）

ISIN 清單頁上市約 9.6 MB、上櫃約 3.2 MB（實測上市下載約 6～13 秒、上櫃 2～24 秒），解析也要時間。3.0 的快取鍵含
`includeWarrant`，含與不含權證各下載一次。3.1 起：

- 每個市場只快取一份完整清單（鍵 `stock_list_{Market}`），型別仍是 `IReadOnlyDictionary<string, StockData>`，
  自訂 `ICacheService`（例如序列化到外部儲存）不必認識新型別。
- 「不含權證」是預設查詢，在讀取時過濾，並以 `ConditionalWeakTable` 依完整清單的實例記住結果：同一份快取只過濾一次，
  快取換新後舊的過濾結果跟著被回收，不會比完整清單活得久。
- 依類型過濾（擴充方法）每次呼叫時過濾；組合太多，不值得記住。
- `SecurityType` 由 CFICode（ISO 10962）前兩碼推得，對照以兩個清單頁實際出現的代碼核對：`ES` 普通股（含創新板、KY 股）、
  `EP` / `EF` 特別股、`ED` TDR、`CE` ETF、`CM` ETN、`CB` REIT 與 `DA` 資產基礎證券（台灣都稱「受益證券」）、`RW` 權證。
  `CM` 在標準中是「其他集合投資工具」，兩個市場目前只有 ETN 使用；原始代碼另以 `StockData.CfiCode` 公開，
  分類不夠細或上游出現新代碼時，呼叫端不必等函式庫更新。

### 合併同時發生的未命中（single-flight，3.1 起）

快取只能擋住「之後」的查詢：程式啟動時十個請求同時查清單，十個都未命中，各自下載同一頁。
`MemoryCacheService` 以帶 token 的 `GetOrSetAsync` 合併同一個 key 的並行未命中：

- 第一個未命中的呼叫開始載入，之後的呼叫加入等候；結果寫入快取後才從「進行中」移除，之後的呼叫一定命中快取或開始新的載入。
- factory 收到的是**這次載入自己的 token**，不是某個呼叫端的：第一個呼叫端取消時只有它自己離開，其他人的下載不受影響；
  所有等候者都離開時才取消上游請求。原本的 `GetOrSetAsync(key, Func<Task<T>>)` 讓 factory 捕捉呼叫端的 token，
  合併後會讓一個人的取消變成所有人的失敗，因此維持不合併。
- 例外與 `null` 交給這次的所有等候者，但都不快取：下一個呼叫重新查詢，與單一呼叫時的語意相同。
- `RefreshAsync`（`QuoteOptions.NoCache`）不參與合併：它的語意就是「一律向上游查詢」，`null` 照舊不覆寫快取。
- 合併放在快取而不是資料來源：報價、歷史、清單同一套機制，資料來源只要把 token 傳給 Fetcher。

---

## 10. 成交價追蹤（WatchAsync）

2.x 的 `IStockPriceObserver` / `StockPriceSubject` 是「呼叫端自己查報價時順帶比價通知」：沒有人查就不會通知，
服務還得保存訂閱狀態（因此曾踩到 Scoped 生命週期與執行緒安全的問題）。3.0 改為由呼叫端掌控的 `IAsyncEnumerable`：

```csharp
await foreach (var quote in stock.WatchAsync("2330", MarketType.TSE, ct: stoppingToken))
    Console.WriteLine(quote.LastPrice);
```

- 先回傳目前報價，之後每隔 `interval`（預設與最短 5 秒，對齊 MIS 的更新頻率）以 `QuoteOptions.NoCache` 查詢，只在成交價變動時回傳。
- 暫時失敗記錄後重試；`NOT_FOUND`（代號打錯）拋出 `StockQueryException` 結束；取消拋出 `OperationCanceledException`。
- 輪詢間隔走 `TimeProvider`，測試以 `FakeTimeProvider` 推進時間，不必真的等待。
- 服務本身因此不再有任何狀態。

多檔追蹤（3.1 起）`WatchAsync(IEnumerable<(Symbol, Market)>)`：每檔各開一個 `WatchAsync` 時，每檔每輪一個請求，
受每主機 3 個 / 5 秒的限流，10 檔一輪要約 15 秒。多檔多載每一輪以批次報價一次查完，同一市場每 50 檔一個請求。

- 每一檔第一次查到時回傳一次，之後只回傳變動的那幾檔；同一輪內依傳入順序，型別仍是 `StockQuote`（`Symbol` 已足以分辨）。
- **還沒查到過**的代號回 `NOT_FOUND` 時拋出——與單檔一致，打錯的代號不該默默被忽略；
  **已經查到過**的代號之後查無資料只記錄、不中斷：一檔的異常不該讓其他幾十檔的追蹤跟著結束。
- 某個市場暫時失敗只影響該市場的代號，記錄（同一個失敗合併成一筆）後下一輪重試。
- 單檔多載的行為完全不變（仍走單檔查詢與單檔快取鍵）。

### 10.1 批次報價與自動判斷市場

**批次報價的回傳型別**：`IReadOnlyDictionary<string, StockResult<StockQuote>>`，每個傳入的代號恰好一筆。

- 考慮過 `StockResult<IReadOnlyDictionary<string, StockQuote>>`（整批成功 / 失敗、查無的代號不在字典中）：
  一個市場失敗就丟掉另一個市場的結果，查無資料也只能靠「不在字典中」表達。改成每檔一個 `StockResult`，
  查無資料、不支援的市場、上游失敗都用既有的錯誤碼表達，呼叫端的處理方式與單檔查詢一樣。
- 考慮過依輸入順序回傳 `IReadOnlyList<StockResult<StockQuote>>`：失敗的結果沒有代號，呼叫端得自己用索引對回去。
  以代號為 key 最直接；同一代號不可能同時屬於兩個市場，指定了兩個市場直接拋 `ArgumentException`。
- 不另外包一個外層 `StockResult`：所有失敗都是逐檔的，沒有「整批失敗」這種狀態。
- 上游失敗時，同一市場這次查詢的每一檔共用同一個失敗結果：資料來源的批次方法是全有或全無的
  （內建來源某一塊失敗就整批拋出），不在介面上暴露分塊細節。成功的塊已寫入快取，重試時不會重查。

**資料來源**：`IStockDataSource.FetchRealtimeQuotesAsync` 以預設介面實作逐檔呼叫單檔方法，第三方來源照常編譯、行為正確，
只是沒有省下請求。內建來源覆寫：

- 每 50 檔一個 MIS 請求（`ex_ch` 以「|」串接，上游依序每檔回一筆）。實測（2026-10）150 檔（URL 約 1.9 KB）可以、
  200 檔（約 2.5 KB）回 rtcode 9999「參數不足」；取 50 留足餘裕，6 碼代號的 URL 也只有約 0.8 KB。
- 快取仍以每一檔為單位（與單檔共用鍵）：先逐檔查快取，只把沒快取的代號送上游，查到的再逐檔寫回。
  `ICacheService` 沒有「只讀」的方法，以「工廠回 `null`」的 `GetOrSetAsync` 查快取——`null` 依約定不會被快取。
- 回應依代號（`c`）對回查詢，不依位置；MIS 的代號區分大小寫（`00631L` 查得到、`00631l` 查不到），回應與查詢完全相同。
- **不把上市與上櫃合併成同一個請求**：MIS 其實接受 `tse_2330.tw|otc_6510.tw`，但那要讓一個資料來源替另一個市場查詢，
  破壞「每市場一個來源」（OCP）。混合清單每輪多一個請求，在限流額度內，划算。

**自動判斷市場**：不指定 `MarketType` 的多載依股票清單判斷代號屬於哪個市場。

- 清單用 `GetStockListAsync(market, includeWarrant: false)`，與呼叫端預設的清單查詢共用快取（預設 1 天），不另外維護對照表。
- 依註冊順序逐一取得清單，全部找到就停：只查上市股票時不會下載上櫃清單。第一次呼叫的成本是下載清單（上市約 9.7 MB，實測 12～78 秒，其中 AngleSharp 解析約 1.5 秒，其餘是傳輸；上櫃約 3.2 MB），
  文件建議啟動時以 `GetAllStockListAsync()` 預熱。
- 不含權證：含權證的清單多出大量權證，而且與不含權證的清單快取鍵不同，會再下載一次同一頁。權證、指數（`t00`）不在清單中，
  回傳 `NOT_FOUND`，請改用指定市場的多載。
- 有清單取得失敗、其他清單又找不到時，回傳該失敗而非 `NOT_FOUND`：代號可能就在沒取得的那份清單裡。
- 不公開「查代號屬於哪個市場」的方法：`GetStockListAsync` / `GetAllStockListAsync` 已能做到，多一個公開成員就多一個 SemVer 承諾。

---

## 11. 設計模式總表

| 模式 | 用於 | 解決的問題 |
|------|------|-----------|
| **Facade** | `StockMarketService` | 隱藏市場選擇、快取、解析編排與錯誤收斂 |
| **Strategy（每市場一實作）** | `IStockDataSource` | 不同市場可獨立演進，零修改新增 / 替換來源（OCP） |
| **Template Method** | `TwseFamilyDataSourceBase` | 證交所家族共用流程，子類別只提供差異 |
| **Separation: Builder/Executor 類比** | `TwseStockParser` ↔ `IStockHttpFetcher` | 純解析與 IO 解耦，解析可單元測試 |
| **Result Pattern** | `StockResult<T>` | 統一成功/失敗回傳，呼叫端免到處 try-catch |
| **Parameter Object** | `QuoteOptions` | 取代布林旗標參數，新增選項不必增加多載 |
| **Async Stream** | `WatchAsync` | 以 `await foreach` 取代有狀態的 Observer |
| **Cache-Aside** | `ICacheService.GetOrSetAsync` / `RefreshAsync` | 減少對證交所的重複請求 |
| **Single-flight** | `MemoryCacheService`（帶 token 的 `GetOrSetAsync`） | 同一 key 的並行未命中只查一次上游 |
| **Options Pattern** | `TwStockOptions` + `IValidateOptions` | 機制參數集中、有預設值、可覆寫並在啟動時驗證 |
| **Chain of Responsibility** | `RateLimitingHandler`（`DelegatingHandler`） | 限流、重試等橫切關注點掛在 HttpClient 管線，Fetcher 不必知道 |
| **DI Extension** | `AddTwStock()` | 一行接好所有相依 |

---

## 12. 演進紀錄

### 1.x → 2.0

| 議題（1.x） | 舊做法 | 2.0 |
|---|---|---|
| 市場分支 | 一個 Strategy 內 `if TSE else OTC` | 每市場一個 `IStockDataSource`，字典路由 |
| 解析無法測 | HTTP + 解析混在一起 | Parser 純函式，餵 fixture 單測 |
| 對外無介面 | README 寫 `IStockMarketService` 但不存在 | 補上介面並實作 |
| 回傳不一致 | 有時 `null`、有時丟例外 | 統一 `StockResult<T>` |
| 不可取消 | 無 `CancellationToken` | 全鏈貫穿 |
| 無測試/CI | 無 | Core/Twse 測試專案 + CI + 自動發 NuGet |
| 依賴較重 | Newtonsoft.Json | System.Text.Json |

### 2.x → 3.0

| 議題（2.x） | 舊做法 | 3.0 |
|---|---|---|
| 公開面過大 | Parser、Fetcher、DataSource、基底類別都是 public | 實作全 `internal`，只公開服務、模型與擴充點 |
| 可變模型 | 屬性可寫、陣列共用，快取可能被呼叫端改壞 | 不可變 record + 唯讀集合 |
| 型別不精確 | `DateTime` 表日期、`string` 表漲跌、`uint` 量、平行陣列表五檔 | `DateOnly`、`decimal? Change`、`long`、`PriceLevel` |
| 旗標參數 | `bypassCache` 在三個介面各加一個多載 | `QuoteOptions`；`ICacheService.RefreshAsync` |
| 有狀態的 Observer | 訂閱存在服務實例上 | `WatchAsync`，服務無狀態 |
| 命名空間分散 | `Models` / `Services` / `Observer` / `Cache` | `TWStockLib` + `TWStockLib.Abstractions`；DI 擴充放 `Microsoft.Extensions.DependencyInjection` |
| `default(MarketType)` 是上櫃 | `OTC = 0` | `Unknown = 0` |

### 3.0 → 3.1（不破壞相容）

| 議題（3.0） | 舊做法 | 3.1 |
|---|---|---|
| 快取時間寫死 | `TwseFamilyDataSourceBase` 內的常數 | `TwStockOptions`，預設值不變 |
| 未命名 HttpClient | 預設逾時、無 User-Agent、無法單獨設定 | 具名 client `TwStockOptions.HttpClientName`，明確逾時與 User-Agent |
| 沒有限流 | 長區間歷史、多檔追蹤可能連續打上游，被封鎖 IP | 每主機 3 個請求 / 5 秒，排隊等候 |
| 加不了重試 | 拿不到 client 的 builder | 以 client 名稱自行加 `AddStandardResilienceHandler()`，重試也經過限流 |
| 清單重複下載 | 含 / 不含權證各下載解析一次數 MB 的 ISIN 頁 | 快取完整清單，讀取時過濾 |
| 並行未命中 | 同時的 N 個查詢各打一次上游 | `MemoryCacheService` single-flight，取消只影響自己 |
| 清單資訊太少 | 只有代號、名稱、市場別；權證以外無法分類 | `SecurityType`、`Industry`、`Isin`、`ListedDate`、`CfiCode`，可依類型過濾 |
| 多檔報價很慢 | 一檔一個 MIS 請求，加上限流後 10 檔約 15 秒 | `GetRealtimeQuotesAsync` / 多檔 `WatchAsync`，同一市場每 50 檔一個請求 |
| 一定要知道市場 | 每個查詢都要傳 `MarketType` | 不指定市場的多載，依快取的股票清單判斷 |
| 代號直接組進 URL | `2330.tw\|tse_2317` 會多查一檔 | 代號格式檢查（英數字、最多 10 碼）+ URL 跳脫 |
| MIS 拒絕查詢被當成查無資料 | `rtcode` 9999 時回 `NOT_FOUND`，`WatchAsync` 因此結束 | `UPSTREAM_ERROR`，追蹤會重試 |

---

## 13. Mechanism vs Policy

> **Library 管「如何跟證交所拿資料、怎麼解析」（Mechanism）；應用層管「拿哪支股、怎麼顯示、漲跌幾趴要告警」（Policy）。**

| 功能 | 層級 | 理由 |
|------|------|------|
| Big5 解碼、URL 組裝、JSON/HTML 解析、單位換算 | **Library** | 與資料來源協定有關 |
| 快取 TTL、限流頻率 | **Library 給預設值，應用層可經 `TwStockOptions` 覆寫**（3.1 起） | 機制面，保護上游不被打爆；預設值放在函式庫，特殊環境（共用 IP、自建代理）再調整 |
| 追蹤的最短間隔 | **Library**（寫死 5 秒） | 對齊 MIS 的更新頻率，再短也拿不到新資料 |
| 重試、斷路器、逾時策略 | **Application**（經 `TwStockOptions.HttpClientName` 加在 HttpClient 上） | 取捨因部署環境而異；函式庫不強迫相依 Polly |
| 要追蹤哪些股票代碼 | **Application** | 業務決策 |
| 漲跌超過 X% 要不要通知 | **Application** | 業務語意，寫在 `await foreach WatchAsync` 的迴圈裡 |
| 報價數字的顯示格式 | **Application** | Presentation |

---

## 14. 刻意不做的事（避免過度設計）

- ❌ 不引入 Bridge / Orchestrator / 泛型 Template-Method Strategy —— 查詢維度固定，收益 < 成本。
- ❌ 不內建背景服務（`HostedService`）推播 —— `WatchAsync` 已讓呼叫端在自己的 `BackgroundService` 裡用一個 `await foreach` 完成。
- ❌ 不為了「看起來厲害」而拆超過兩個 connector 套件 —— 目前證交所家族放同一個 `TWStockLib.Twse` 即可。
- ❌ 不抽象 `ICacheService` 以外的儲存後端 —— `MemoryCache` 夠用，需要 Redis 再說。
- ❌ 不內建重試 / 斷路器 —— 只提供具名 HttpClient 讓使用者自己加，避免強迫相依 Polly，也避免替使用者決定重試策略。
- ❌ 不提供逐主機的限流設定 —— 四個上游主機共用同一組 `PermitLimit` / `Window`，各自計算；目前沒有個別調整的需求。
- ❌ 不把上市與上櫃的報價合併成同一個 MIS 請求 —— 會讓一個資料來源替別的市場查詢；混合清單每輪多一個請求而已。
- ❌ 自動判斷市場不涵蓋權證與指數 —— 含權證的清單要另外下載、解析一次，筆數也多上許多；這些代號請指定市場。
