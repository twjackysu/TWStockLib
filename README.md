# TWStockLib

[![NuGet](https://img.shields.io/nuget/v/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![CI](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml/badge.svg)](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/twjackysu/TWStockLib.svg)](LICENSE)

TWStockLib 是一個用於獲取台灣股市資料的 .NET 類別庫，提供股票清單、歷史數據、即時報價與成交價追蹤。

- 全非同步、可取消（`async` + `CancellationToken`）
- 統一以 `StockResult<T>` 回傳成功 / 失敗，呼叫端免到處 try-catch；檢查 `IsSuccess` 後即可直接使用 `Value`
- 模型都是不可變的 record，可以放心共用與快取
- 每個市場一個資料來源，新增市場零修改現有程式碼（OCP）
- 純函式解析層（與 IO 分離），可離線單元測試
- 支援 `net8.0` / `net9.0` / `net10.0`

> 架構與設計模式說明見 [DESIGN.md](DESIGN.md)。

## 安裝

```
dotnet add package TWStockLib
```

`TWStockLib` 會自動帶入核心套件 `TWStockLib.Core`。若只需要核心抽象與模型（不含證交所來源實作），可單獨安裝：

```
dotnet add package TWStockLib.Core
```

## 功能特點

- 獲取上市（TSE）與上櫃（OTC）股票清單，預設排除權證與牛熊證
- 獲取股票歷史日成交資料（跨月自動合併，上市與上櫃單位一致）
- 獲取股票即時報價（含最佳五檔）
- 以 `await foreach` 追蹤成交價變動
- 內建快取，減少對上游的請求
- 內建每主機限流，長區間歷史查詢與多檔追蹤不會讓證交所封鎖你的 IP
- 快取時間、限流可設定；可在 HttpClient 上自行加上重試（不強迫相依 Polly）
- 完整的錯誤處理與日誌記錄

## 快速入門

### 步驟 1：註冊服務

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddTwStock();   // 註冊 TWStockLib 服務（含 TSE / OTC 兩個資料來源），重複呼叫無妨

var provider = services.BuildServiceProvider();
```

### 步驟 2：取得服務

```csharp
using TWStockLib;   // 所有對外型別都在這個命名空間

var stock = provider.GetRequiredService<IStockMarketService>();
```

在 Controller 中透過建構式注入（服務是 Singleton，可以注入到任何生命週期的元件）：

```csharp
public class StockController(IStockMarketService stock) : Controller
{
}
```

### 步驟 3：使用服務

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

#### 股票清單

```csharp
var all = await stock.GetAllStockListAsync();
if (all.IsSuccess)
    Console.WriteLine($"共 {all.Value.Count} 支股票");

var tse = await stock.GetStockListAsync(MarketType.TSE);
var otcWithWarrants = await stock.GetStockListAsync(MarketType.OTC, includeWarrant: true);
```

預設排除權證；是否為權證依 ISIN 的 CFICode 判斷，認購、認售與牛熊證都算在內。

#### 歷史資料

```csharp
var history = await stock.GetHistoricalDataAsync(
    "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

foreach (var d in history.GetValueOrThrow())
    Console.WriteLine($"{d.Date:yyyy-MM-dd} 開:{d.OpeningPrice} 收:{d.ClosingPrice} 漲跌:{d.Change}");
```

- 成交量 `TradeVolume` 一律是「股」、成交金額 `TurnoverValue` 一律是「元」，上市與上櫃一致。
- 當日無成交時開高低收為 `null`；除權息等不比價的日子 `Change` 為 `null`。

#### 即時報價

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

#### 追蹤成交價

```csharp
await foreach (var quote in stock.WatchAsync("2330", MarketType.TSE, ct: stoppingToken))
    Console.WriteLine($"{quote.SyncTime:HH:mm:ss} {quote.Symbol} {quote.LastPrice}");
```

- 先回傳一次目前的報價，之後每隔 `interval`（預設與最短皆為 5 秒）略過快取查詢，只在成交價變動時回傳。
- 上游暫時失敗會記錄後在下一輪重試；查無此代號時拋出 `StockQueryException`（`ErrorCode` 為 `NOT_FOUND`）。
- 以 `CancellationToken` 結束追蹤，結束時拋出 `OperationCanceledException`。

## 設定

`AddTwStock` 可帶入設定，調整快取時間與限流；沒有設定的項目維持預設值（與 3.0 的行為相同）：

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
因此內建限流預設採用這個頻率，並對每個上游主機（`www.twse.com.tw`、`mis.twse.com.tw`、`www.tpex.org.tw`、
`isin.twse.com.tw`）分開計算：任何 5 秒內最多送出 3 個請求，超過的請求排隊等候，不會失敗。

- 歷史資料一個月一個請求：查一整年（12 個月）約需 15 秒。查歷史不會拖慢即時報價，兩者是不同主機。
- `WatchAsync` 同時追蹤超過 3 檔時，每檔的實際更新間隔會被拉長（例如 6 檔約每 10 秒一次）。
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
        // 股票清單頁有數 MB、上游傳得慢（實測 3.2 MB 約 24 秒），排隊等限流的時間也算在內，
        // 標準預設（單次 10 秒、總計 30 秒）太短
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

- **替換快取**：在 `AddTwStock()` 之前註冊自己的 `ICacheService`，會保留你的實作。

## 錯誤碼

`StockResult<T>.ErrorCode` 可能的值（見 `StockErrorCodes`）：

| 錯誤碼 | 意義 |
|--------|------|
| `SOURCE_NOT_FOUND` | 找不到對應市場的資料來源 |
| `NOT_FOUND` | 上游查無此股票 / 無資料 |
| `UPSTREAM_ERROR` | 上游連線或回應錯誤 |
| `TIMEOUT` | 上游在 HttpClient 逾時內沒有回應（2.3.0 起） |
| `PARSE_ERROR` | 上游回應的格式無法解析，例如被擋下時回傳的錯誤頁（3.0.0 起） |

## 支援的市場

- TSE（台灣證券交易所，上市）
- OTC（櫃買中心，上櫃）

## 注意事項

1. 所有資料皆從網路即時獲取，需要連線。
2. 歷史數據以月為單位查詢，跨月會自動合併。
3. 即時報價快取 30 秒、股票清單與已結束月份的歷史快取 1 天、當月歷史快取 30 分鐘，以減少上游請求，可由 `TwStockOptions` 調整（見[設定](#設定)）。即時報價可用 `QuoteOptions.NoCache` 略過快取。
4. 歷史資料的成交量一律是「股」、成交金額一律是「元」，上市與上櫃單位一致。
5. 對每個上游主機預設每 5 秒最多 3 個請求，超過的請求排隊等候（見[限流](#限流)）。

## 版本紀錄

完整說明見 [GitHub Releases](https://github.com/twjackysu/TWStockLib/releases)。

| 版本 | 變更 |
|------|------|
| 3.0.0 | 破壞性變更：不可變 record 模型、`DateOnly`、`QuoteOptions`、以 `WatchAsync` 取代觀察者、命名空間整併、實作類別改為 internal（見下方升級說明） |
| 2.3.1 | 查無代號、或代號不屬於指定的市場（例如以 `MarketType.OTC` 查 2330）時，即時報價改回 `NOT_FOUND`；先前回傳欄位全空的成功結果 |
| 2.3.0 | 修正多項資料與行為錯誤：上櫃歷史單位、牛熊證過濾、語系相依的解析、取消與逾時、價格訂閱的執行緒安全與生命週期等；AngleSharp 最低版本升至 1.5.0（修補 GHSA-pgww-w46g-26qg） |
| 2.2.1 | 套件內 DLL 的組件版本改為與套件版本一致（2.2.0 的 DLL 仍標示為 2.1.1），功能與 2.2.0 相同 |
| 2.2.0 | 即時報價新增 `bypassCache` 參數，可略過內部 30 秒快取；新增成員皆有預設介面實作，既有實作不需修改 |
| 2.1.1 | 證交所 / 櫃買中心端點改走 https |
| 2.1.0 | 新增 `net10.0` 支援 |
| 2.0.0 | 架構重構，API 有破壞性變更（見下方升級說明） |

## 從 2.x 升級到 3.0

3.0 是 API 整理的破壞性版本。多數情況只要照下表替換即可：

| 2.x | 3.0 |
|-----|-----|
| `using TWStockLib.Models;` / `.Services;` / `.Observer;` | `using TWStockLib;`（`AddTwStock()` 在 `Microsoft.Extensions.DependencyInjection`，不必另外 using） |
| `AddStockServices()` | `AddTwStock()` |
| `GetHistoricalDataAsync(symbol, DateTime start, DateTime end, market)` | `GetHistoricalDataAsync(symbol, market, DateOnly from, DateOnly to)` |
| `GetRealtimeQuoteAsync(symbol, market, bypassCache: true)` | `GetRealtimeQuoteAsync(symbol, market, QuoteOptions.NoCache)` |
| `SubscribePriceChanges` / `IStockPriceObserver` / `ConsoleStockPriceObserver` | `await foreach (var q in stock.WatchAsync(symbol, market, ct: ct))` |
| `result.Value!` | 檢查 `IsSuccess` 後直接用 `result.Value`，或 `TryGetValue` / `GetValueOrThrow` |
| `StockQuote.Top5SellPrice` / `Top5SellVolume` / `Top5BuyPrice` / `Top5BuyVolume` | `StockQuote.Asks` / `Bids`（`PriceLevel(Price, Volume)`） |
| `StockQuote.SyncTime`（`DateTime`，UTC 但 Kind 未指定） | `DateTimeOffset`（`+08:00`） |
| `StockHistory.Date`（`DateTime`） | `DateOnly` |
| `StockHistory.TurnOverInValue` | `TurnoverValue` |
| `StockHistory.DailyPricing`（`string`） | `Change`（`decimal?`，不比價為 `null`） |
| 無成交日的開高低收為 `0` | `null` |
| 成交量、筆數為 `uint` | `long` |
| `StockQuote` 繼承 `StockData` | 兩者為獨立的 record |
| 模型屬性可修改 | 不可變 record，需要修改時用 `with` 產生副本 |
| `MarketType`：`OTC = 0`、`TSE = 1` | `Unknown = 0`、`TSE = 1`、`OTC = 2`（若曾以整數保存，請轉換） |
| 上游格式無法解析時回 `UPSTREAM_ERROR` | `PARSE_ERROR` |
| 空白代號、起日晚於迄日照樣查詢 | 拋出 `ArgumentException` |
| 查無代號時回傳欄位全空的報價（2.3.0 以前；2.3.1 已修正） | `NOT_FOUND` |

自訂 `IStockDataSource` / `ICacheService` 的實作者另需注意：

- `IStockDataSource.FetchRealtimeQuoteAsync` 改為 `(symbol, QuoteOptions options, ct)`，請遵守 `options.BypassCache`；
  `FetchHistoricalDataAsync` 改為 `(symbol, DateOnly from, DateOnly to, ct)`；回傳的集合請用不可變集合。
- `ICacheService` 改為 `GetOrSetAsync(key, factory, expiry)` 與 `RefreshAsync(key, factory, expiry)` 兩個方法，`expiry` 必填。
- 原本公開的 `TwseStockDataSource`、`TpexStockDataSource`、`TwseFamilyDataSourceBase`、`IStockParser`、
  `TwseStockParser`、`IStockHttpFetcher`、`TwseHttpFetcher` 改為 internal，請改用 `AddTwStock()` 取得內建來源。
- `TWStockLib.Cache` 命名空間移除，`ICacheService` / `MemoryCacheService` 移到 `TWStockLib.Abstractions`。

## 從 2.2.x 升級到 2.3.0

API 簽章不變，但以下修正會改變回傳的資料或行為，升級前請確認：

- **上櫃（OTC）歷史資料的單位**：`TradeVolume` 改為「股」、`TurnOverInValue` 改為「元」，與上市一致。
  先前直接沿用櫃買的「仟股」/「仟元」，數值是現在的 1/1000；若你曾自行乘上 1000，請拿掉。
- **`includeWarrant: false` 會一併排除牛熊證**：改依 ISIN 的 CFICode 判斷，先前會漏掉名稱以「牛」/「熊」結尾的牛熊證。
- **歷史資料中格式不符的列會被略過**：先前會以今天日期、價格 0 填入，混進查詢結果。解析也不再受系統語系影響。
- **沒有值的報價欄位回 `null`**：例如指數的 `LimitUp` / `LimitDown`，先前是 `0`。
- **取消會拋出例外**：呼叫端取消時拋出 `OperationCanceledException`，先前回傳 `UPSTREAM_ERROR`。HttpClient 逾時改回 `TIMEOUT`。
- **`IStockMarketService` 改為 Singleton**：價格訂閱不再隨 scope（例如每個 HTTP request）消失，且可多執行緒同時使用。
  觀察者丟出的例外只會記錄，不再讓已取得的報價變成失敗。
- **`AddTwStock()` 可重複呼叫**：呼叫前已註冊的 `ICacheService` 等相依會被保留，可用來替換預設實作。
- **當月歷史資料只快取 30 分鐘**：今天收盤後新增的那一筆不會再被舊快取擋住一整天。
- `StockHistory(List<string>)` 建構式標記為 `[Obsolete]`，將於 3.0 移除。
- 相依的 AngleSharp 最低版本由 1.1.2 升到 1.5.0，修補 mXSS 弱點 [GHSA-pgww-w46g-26qg](https://github.com/advisories/GHSA-pgww-w46g-26qg)。

## 從 1.0.x 升級

2.0 版為架構重構，API 有破壞性變更：

- `AddStockServices()` → `AddTwStock()`（舊名在 2.x 保留但標記 `[Obsolete]`，3.0 移除）
- 服務方法改為非同步並回傳 `StockResult<T>`：
  `GetRealtimeQuote` → `GetRealtimeQuoteAsync`、`GetHistoricalData` → `GetHistoricalDataAsync`、
  `GetStockList` → `GetStockListAsync`、`GetAllStockList` → `GetAllStockListAsync`
- 以 `IStockMarketService` 介面注入（取代具體型別 `StockMarketService`）

## 授權

本專案採用 MIT 授權，詳見 [LICENSE](LICENSE)。
