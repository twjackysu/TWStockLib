# 升級指南

## 從 2.x 升級到 3.0

3.0 是 API 整理的破壞性版本。多數情況只要照下表替換即可：

| 2.x | 3.0 |
|-----|-----|
| `using TWStockLib.Models;` / `.Services;` / `.Observer;` | `using TWStockLib;`（`AddTwStock()` 在 `Microsoft.Extensions.DependencyInjection`，不必另外 using） |
| `AddStockServices()` | `AddTwStock()` |
| `GetHistoricalDataAsync(symbol, DateTime start, DateTime end, market)` | `GetHistoricalDataAsync(symbol, market, DateOnly from, DateOnly to)` |
| `GetRealtimeQuoteAsync(symbol, market, bypassCache: true)` | `GetRealtimeQuoteAsync(symbol, market, QuoteOptions.NoCache)` |
| `GetStockListAsync(market, includeWarrant: true)` / `GetAllStockListAsync(includeWarrant: true)` | `GetStockListAsync(market, StockListOptions.WithWarrants)` / `GetAllStockListAsync(StockListOptions.WithWarrants)` |
| 股票清單來自 ISIN 證券編碼頁 | 預設改用 OpenAPI（快很多，少數停止交易中、尚未掛牌的證券不在其中）；要最完整的清單用 `new StockListOptions { Source = StockListSource.Isin }` |
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
  `FetchHistoricalDataAsync` 改為 `(symbol, DateOnly from, DateOnly to, ct)`；`FetchStockListAsync(bool includeWarrant, ct)`
  改為 `FetchStockListAsync(StockListOptions options, ct)`；回傳的集合請用不可變集合。
- `ICacheService` 改為 `GetOrSetAsync`、`RefreshAsync`（`factory` 會收到 `CancellationToken`，`expiry` 必填）與 `TryGetValue` 三個方法。
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
