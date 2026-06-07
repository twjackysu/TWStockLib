# TWStockLib — 設計文件

> 版本：v0.1 草稿
> 目標：把 TWStockLib 從「單一專案、職責混雜」重構成**職責清楚、可測試、易擴充、可多框架發佈到 NuGet** 的類別庫。
> 設計心法承襲 `DataAnalysisLibrary`，但**刻意不過度設計**——只引入對本 lib 真正有價值的抽象。

---

## 目錄

1. [設計目標與原則](#1-設計目標與原則)
2. [核心心法（從 DataAnalysisLibrary 借什麼、不借什麼）](#2-核心心法)
3. [整體架構分層](#3-整體架構分層)
4. [方案結構（Solution Structure）](#4-方案結構)
5. [核心抽象層](#5-核心抽象層)
6. [Result 模型（StockResult&lt;T&gt;）](#6-result-模型)
7. [每市場一個 DataSource（OCP 擴充）](#7-每市場一個-datasource)
8. [Parser / Fetcher 分離（可測試性）](#8-parser--fetcher-分離)
9. [Observer 模式的定位](#9-observer-模式的定位)
10. [設計模式總表](#10-設計模式總表)
11. [與舊版的對比與改進點](#11-與舊版的對比與改進點)
12. [Mechanism vs Policy — Library 與應用層的邊界](#12-mechanism-vs-policy)
13. [刻意不做的事（避免過度設計）](#13-刻意不做的事)

---

## 1. 設計目標與原則

| 目標 | 說明 |
|------|------|
| **關注點分離** | 抓資料（IO）、解析（純字串→Model）、編排（Service）各自獨立 |
| **低耦合、高內聚** | 新增一個市場（興櫃 / ETF / 期貨…）不需修改現有程式碼（OCP） |
| **可測試性** | 解析邏輯不依賴網路，可餵固定 HTML/JSON fixture 單元測試 |
| **全非同步 + 可取消** | 所有對外 IO 以 `async/await` 實作，並貫穿 `CancellationToken` |
| **型別安全 + 可空標註** | `Nullable=enable`，回傳一致以 `StockResult<T>` 包裝 |
| **可多框架發佈** | `net8.0;net9.0`，標準 NuGet metadata + SourceLink + symbols |

---

## 2. 核心心法

DataAnalysisLibrary 的精神只有兩條，套到本 lib：

1. **純邏輯與 IO 分離**
   DataAnalysisLibrary：`IQueryBuilder`（純字串、可測） ↔ `IQueryExecutor`（碰 SDK / IO）。
   TWStockLib 對應：`IStockParser`（HTML/JSON 字串 → Model，純函式） ↔ `IStockHttpFetcher`（只負責抓網頁）。

2. **每個來源一套實作、集合/字典註冊、OCP 擴充**
   DataAnalysisLibrary：`IDataSourceFactory` 用 `SourceKey` 路由，`AddSource()` 一行新增。
   TWStockLib 對應：**每個市場一個 `IStockDataSource`**，以 `MarketType` 為 key 註冊進字典，`StockMarketService` 只認介面、不再寫 `if (TSE) … else (OTC)`。

**不借**的部分（對本 lib 屬過度設計）：Bridge、QueryOrchestrator、Template-Method Strategy。本 lib 查詢維度固定（報價／歷史／清單），來源同屬證交所家族，這些抽象帶來的成本大於收益。

---

## 3. 整體架構分層

```
┌──────────────────────────────────────────────┐
│            呼叫端（Console / Web API）          │
└───────────────────────┬──────────────────────┘
                        │ 只依賴 IStockMarketService
┌───────────────────────▼──────────────────────┐
│   StockMarketService  (Facade / 編排)          │
│   GetRealtimeQuoteAsync / GetHistoricalAsync   │
│   GetStockListAsync …  回傳 StockResult<T>      │
└───────────────────────┬──────────────────────┘
            │ 依 MarketType 取得
┌───────────▼──────────────────────────────────┐
│   IReadOnlyDictionary<MarketType,             │
│                       IStockDataSource>        │
├───────────────┬───────────────────────────────┤
│ TwseStockData │ TpexStockDataSource │ 未來市場 │
│ Source (TSE)  │ (OTC)               │  …       │
└───────────────┴───────────────────────────────┘
        │ 每個 DataSource 內部再分兩刀：
   ┌────┴─────┐
┌──▼────────┐ ┌▼─────────────────────────────┐
│IStockParser│ │ IStockHttpFetcher            │
│(純字串解析)│ │ (抓網頁 + Big5 編碼，碰 IO)   │
└───────────┘ └──────────────────────────────┘
```

---

## 4. 方案結構

```
TWStockLib.slnx
├── Directory.Build.props                 ← 共用 metadata / 多框架 / Nullable / LangVersion
├── README.md
├── DESIGN.md / PLAN.md
├── src/
│   ├── TWStockLib.Core/                   ← 零外部 SDK 依賴（只靠 BCL + Extensions 抽象）
│   │   ├── Abstractions/
│   │   │   ├── IStockMarketService.cs
│   │   │   ├── IStockDataSource.cs
│   │   │   ├── IStockParser.cs            （泛型：依輸出型別）
│   │   │   ├── IStockHttpFetcher.cs
│   │   │   └── ICacheService.cs
│   │   ├── Models/
│   │   │   ├── StockQuote.cs / StockHistory.cs / StockData.cs
│   │   │   ├── MarketType.cs
│   │   │   └── StockResult.cs             ← Result Pattern
│   │   ├── Observer/  IStockPriceObserver, StockPriceSubject
│   │   ├── Caching/   MemoryCacheService
│   │   └── Services/  StockMarketService（只依賴抽象）
│   └── TWStockLib.Twse/                    ← 證交所家族實作，依賴 AngleSharp
│       ├── DataSources/
│       │   ├── TwseStockDataSource.cs      (TSE)
│       │   └── TpexStockDataSource.cs      (OTC，獨立！)
│       ├── Parsers/
│       │   ├── TwseQuoteParser.cs / TwseHistoryParser.cs / IsinStockListParser.cs
│       ├── Http/      TwseHttpFetcher.cs
│       ├── Internal/  TwseApiModels.cs（msgArray / TPEX table，internal）
│       └── ServiceCollectionExtensions.cs  (AddTwStock)
└── tests/
    ├── TWStockLib.Core.Tests/             ← Service 路由、StockResult
    └── TWStockLib.Twse.Tests/             ← Parser 餵 fixture 字串，離線
```

> **分包理由**：`Core` 完全無第三方 SDK 依賴，單元測試最乾淨；想換解析來源（例如改抓券商 API）只要新增一個 connector 套件，不動 Core。

---

## 5. 核心抽象層

```csharp
// 對外唯一入口（修掉舊版 README 寫了卻不存在的 IStockMarketService）
public interface IStockMarketService
{
    Task<StockResult<StockQuote>> GetRealtimeQuoteAsync(
        string symbol, MarketType market, CancellationToken ct = default);

    Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoricalDataAsync(
        string symbol, DateTime startDate, DateTime endDate, MarketType market,
        CancellationToken ct = default);

    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetStockListAsync(
        MarketType market, bool includeWarrant = false, CancellationToken ct = default);

    Task<StockResult<IReadOnlyDictionary<string, StockData>>> GetAllStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);

    void SubscribePriceChanges(string symbol, IStockPriceObserver observer);
    void UnsubscribePriceChanges(string symbol, IStockPriceObserver observer);
}

// 每個市場一套實作
public interface IStockDataSource
{
    MarketType Market { get; }
    Task<StockQuote?> FetchRealtimeQuoteAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<StockHistory>> FetchHistoricalDataAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, StockData>> FetchStockListAsync(
        bool includeWarrant = false, CancellationToken ct = default);
}

// 純字串 → Model，不碰 IO，好單測
public interface IStockParser
{
    StockQuote? ParseRealtimeQuote(string rawJson, MarketType market);
    IReadOnlyList<StockHistory> ParseHistory(string rawJson, MarketType market);
    IReadOnlyDictionary<string, StockData> ParseStockList(string rawBig5Html, MarketType market, bool includeWarrant);
}

// 只負責抓網頁（含 Big5 編碼處理），碰 IO
public interface IStockHttpFetcher
{
    Task<string> GetStringAsync(string url, CancellationToken ct = default);
    Task<string> GetBig5StringAsync(string url, CancellationToken ct = default);
}
```

---

## 6. Result 模型

```csharp
public sealed class StockResult<T>
{
    public bool    IsSuccess    { get; private init; }
    public T?      Value        { get; private init; }
    public string? ErrorCode    { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static StockResult<T> Ok(T value)
        => new() { IsSuccess = true, Value = value };

    public static StockResult<T> Fail(string code, string message)
        => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };
}
```

常見錯誤碼：`SOURCE_NOT_FOUND`（不支援的市場）、`NOT_FOUND`（查無此股）、`UPSTREAM_ERROR`（證交所回非 OK / 連線失敗）、`PARSE_ERROR`（格式變動解析失敗）。

> 採用理由：對齊 DataAnalysisLibrary 的 `QueryResult<T>`，呼叫端不用在每次呼叫包 try-catch，網路與格式錯誤都收斂成一致回傳。

---

## 7. 每市場一個 DataSource

舊版 `TwseDataFetchStrategy` 內部用 `if (marketType == TSE) … else …` 硬分支處理兩個市場，名為 Strategy 實為 god-class。新版每個市場各自獨立：

```csharp
public sealed class TpexStockDataSource : IStockDataSource
{
    public MarketType Market => MarketType.OTC;
    private readonly IStockHttpFetcher _fetcher;
    private readonly IStockParser _parser;
    private readonly ICacheService _cache;
    // 只關心 OTC 自己的 URL / 編碼 / 解析
}
```

註冊（DI）：

```csharp
public static IServiceCollection AddTwStock(this IServiceCollection services)
{
    services.AddHttpClient();
    services.AddMemoryCache();
    services.AddSingleton<ICacheService, MemoryCacheService>();
    services.AddSingleton<IStockParser, TwseStockParser>();
    services.AddSingleton<IStockHttpFetcher, TwseHttpFetcher>();

    services.AddSingleton<IStockDataSource, TwseStockDataSource>();
    services.AddSingleton<IStockDataSource, TpexStockDataSource>();   // ← 新增市場只多這一行

    services.AddScoped<IStockMarketService, StockMarketService>();
    return services;
}
```

`StockMarketService` 建構時把 `IEnumerable<IStockDataSource>` 收成 `Dictionary<MarketType, …>`，查詢時依 `MarketType` 取用——**新增市場零修改現有程式碼（OCP）**。

---

## 8. Parser / Fetcher 分離

舊版把「抓網頁」「Big5 解碼」「JSON / HTML 解析」「`GetDecimal/GetUInt32Array` 一堆 helper」全塞在一個檔案裡，無法離線測試。新版：

- `IStockHttpFetcher` → 唯一碰 `HttpClient` 的地方，含 `Encoding.RegisterProvider` 與 Big5（CodePage 950）處理。
- `IStockParser` → 純函式，吃字串吐 Model。原本的 `GetNullableDecimal / GetDecimalArray / GetUInt32Array` 等 helper 全部移進來，變成可單測對象。
- 測試只要把證交所某次的真實 JSON/HTML 存成 fixture，餵進 Parser 斷言輸出，**不需要網路、穩定、快**。

---

## 9. Observer 模式的定位

保留 `IStockPriceObserver` / `StockPriceSubject`，但文件要誠實說明：目前是「呼叫 `GetRealtimeQuoteAsync` 時順帶比價通知」，屬 pull 觸發，非背景 push。若未來要真背景輪詢，再加一個 `IStockPriceMonitor`（背景 `HostedService`）即可，先不做（見第 13 節）。

---

## 10. 設計模式總表

| 模式 | 用於 | 解決的問題 |
|------|------|-----------|
| **Facade** | `StockMarketService` | 隱藏市場選擇、快取、解析編排 |
| **Strategy（每市場一實作）** | `IStockDataSource` | 不同市場可獨立演進，零修改新增來源（OCP） |
| **Separation: Builder/Executor 類比** | `IStockParser` ↔ `IStockHttpFetcher` | 純解析與 IO 解耦，解析可單元測試 |
| **Observer** | `StockPriceSubject` | 價格變動通知訂閱者 |
| **Result Pattern** | `StockResult<T>` | 統一成功/失敗回傳，呼叫端免到處 try-catch |
| **Cache-Aside** | `ICacheService.GetOrSetAsync` | 減少對證交所的重複請求 |
| **DI Extension** | `AddTwStock()` | 一行接好所有相依 |

---

## 11. 與舊版的對比與改進點

| 議題（舊版） | 舊做法 | 新做法 |
|---|---|---|
| 市場分支 | 一個 Strategy 內 `if TSE else OTC` | 每市場一個 `IStockDataSource`，字典路由 |
| 解析無法測 | HTTP + 解析混在一起 | `IStockParser` 純函式，餵 fixture 單測 |
| 對外無介面 | README 寫 `IStockMarketService` 但不存在 | 真的補上介面並實作 |
| 回傳不一致 | 有時 `null`、有時丟例外 | 統一 `StockResult<T>` |
| 不可取消 | 無 `CancellationToken` | 全鏈貫穿 |
| 單一框架 | `net8.0`、`Nullable=disable` | `net8.0;net9.0`、`Nullable=enable` |
| 無測試/CI | 無 | Core/Twse 測試專案 + CI + 自動發 NuGet |
| 依賴較重 | Newtonsoft.Json | System.Text.Json（砍依賴） |
| API 模型外洩 | 放在 Strategy 公開檔 | 移到 Twse 專案 `internal` |

---

## 12. Mechanism vs Policy

> **Library 管「如何跟證交所拿資料、怎麼解析」（Mechanism）；應用層管「拿哪支股、怎麼顯示、漲跌幾趴要告警」（Policy）。**

| 功能 | 層級 | 理由 |
|------|------|------|
| Big5 解碼、URL 組裝、JSON/HTML 解析 | **Library** | 與資料來源協定有關 |
| 快取 TTL 預設值 | **Library**（可由 options 覆寫） | 機制面，但允許 Policy 調整 |
| 要監控哪些股票代碼 | **Application** | 業務決策 |
| 漲跌超過 X% 要不要通知 | **Application** | 業務語意，放進 Observer 實作 |
| 報價數字的顯示格式 | **Application** | Presentation |

---

## 13. 刻意不做的事（避免過度設計）

- ❌ 不引入 Bridge / Orchestrator / 泛型 Template-Method Strategy —— 查詢維度固定，收益 < 成本。
- ❌ 不做背景輪詢推播（`IStockPriceMonitor` / HostedService）—— 等有實際需求再加，介面已預留空間。
- ❌ 不為了「看起來厲害」而拆超過兩個 connector 套件 —— 目前證交所家族放同一個 `TWStockLib.Twse` 即可。
- ❌ 不抽象 `ICacheService` 以外的儲存後端 —— `MemoryCache` 夠用，需要 Redis 再說。
