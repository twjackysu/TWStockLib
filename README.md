# TWStockLib

[![NuGet](https://img.shields.io/nuget/v/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![CI](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml/badge.svg)](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/twjackysu/TWStockLib.svg)](LICENSE)

TWStockLib 是一個用於獲取台灣股市資料的 .NET 類別庫，提供股票清單、歷史數據與即時報價，並支援價格變化的觀察者模式。

- 全非同步、可取消（`async` + `CancellationToken`）
- 統一以 `StockResult<T>` 回傳成功 / 失敗，呼叫端免到處 try-catch
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

- 獲取上市（TSE）與上櫃（OTC）股票清單
- 獲取股票歷史數據（跨月自動合併）
- 獲取股票即時報價
- 監控股票價格變化（觀察者模式）
- 內建快取，減少對上游的請求
- 完整的錯誤處理與日誌記錄

## 快速入門

### 步驟 1：註冊服務

```csharp
using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Services;

var services = new ServiceCollection();
services.AddLogging();
services.AddTwStock();   // 註冊 TWStockLib 服務（含 TSE / OTC 兩個資料來源）

var provider = services.BuildServiceProvider();
```

### 步驟 2：取得服務

在 Console 中：

```csharp
var stock = provider.GetRequiredService<IStockMarketService>();
```

在 Controller 中透過建構式注入：

```csharp
public class StockController : Controller
{
    private readonly IStockMarketService _stock;
    public StockController(IStockMarketService stock) => _stock = stock;
}
```

### 步驟 3：使用服務

所有查詢方法皆為非同步，並回傳 `StockResult<T>`：以 `IsSuccess` 判斷成功，失敗時帶 `ErrorCode` / `ErrorMessage`。
呼叫端用 `CancellationToken` 取消時，照 .NET 慣例拋出 `OperationCanceledException`，不會變成失敗結果。
回傳的模型是呼叫端專屬的複本，修改它們不會影響函式庫內部的快取。

#### 獲取股票清單

```csharp
var all = await stock.GetAllStockListAsync();
if (all.IsSuccess)
    Console.WriteLine($"共 {all.Value!.Count} 支股票");

var tse = await stock.GetStockListAsync(MarketType.TSE);
var otc = await stock.GetStockListAsync(MarketType.OTC);
```

#### 獲取歷史數據

```csharp
var history = await stock.GetHistoricalDataAsync(
    "2330",
    new DateTime(2023, 1, 1),
    new DateTime(2023, 1, 31),
    MarketType.TSE);

if (history.IsSuccess)
{
    foreach (var d in history.Value!)
        Console.WriteLine($"{d.Date:yyyy-MM-dd} 開:{d.OpeningPrice} 收:{d.ClosingPrice}");
}
```

#### 獲取即時報價

```csharp
var result = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);
if (result.IsSuccess)
{
    var q = result.Value!;
    Console.WriteLine($"{q.Symbol} {q.Name} 最新價:{q.LastPrice} 最高:{q.HighestPrice} 最低:{q.LowestPrice}");
}
else
{
    Console.WriteLine($"取得失敗：{result.ErrorCode} - {result.ErrorMessage}");
}
```

即時報價預設會在函式庫內快取 30 秒，同一檔股票在這段時間內重複查詢不會再打上游。
呼叫端自己已有節流、或需要「這一刻」的值時，可以略過快取（2.2.0 起）：

```csharp
// 略過內部快取、一律向證交所查詢；查到的新報價仍會更新快取，之後的一般呼叫會拿到它
var fresh = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE, bypassCache: true);
```

> 上游端點（mis.twse.com.tw）本身有頻率限制，略過快取後的呼叫頻率由呼叫端負責。
> 加權指數在 MIS 的代號是 `t00`，直接當作上市（`MarketType.TSE`）的一檔查即可：
> `GetRealtimeQuoteAsync("t00", MarketType.TSE, bypassCache: true)`；`LastPrice` 是指數、
> `YesterdayClosingPrice` 是昨收。盤前尚未有第一筆成交時 `LastPrice` 為 `null`。
> `SyncTime` 是 `tlong` 換算成的 UTC 時間（`DateTimeKind.Unspecified`）。

#### 使用觀察者模式監控價格變化

```csharp
using TWStockLib.Observer;

public class MyObserver : IStockPriceObserver
{
    public void OnPriceChanged(string symbol, decimal newPrice, decimal oldPrice)
    {
        var pct = (newPrice - oldPrice) / oldPrice * 100;
        Console.WriteLine($"{symbol} {(newPrice > oldPrice ? "上漲" : "下跌")}：{oldPrice} → {newPrice} ({pct:F2}%)");
    }
}

var observer = new MyObserver();
stock.SubscribePriceChanges("2330", observer);

// 連續取得報價時，價格變動會通知觀察者
await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);

stock.UnsubscribePriceChanges("2330", observer);
```

> 內建 `ConsoleStockPriceObserver`（於 `TWStockLib.Observer.DefaultProvidedObservers`）可直接使用。

## 錯誤碼

`StockResult<T>.ErrorCode` 可能的值（見 `StockErrorCodes`）：

| 錯誤碼 | 意義 |
|--------|------|
| `SOURCE_NOT_FOUND` | 找不到對應市場的資料來源 |
| `NOT_FOUND` | 上游查無此股票 / 無資料 |
| `UPSTREAM_ERROR` | 上游連線或回應錯誤 |
| `TIMEOUT` | 上游在 HttpClient 逾時內沒有回應（2.3.0 起） |

## 支援的市場

- TSE（台灣證券交易所，上市）
- OTC（櫃買中心，上櫃）

## 注意事項

1. 所有資料皆從網路即時獲取，需要連線。
2. 歷史數據以月為單位查詢，跨月會自動合併。
3. 即時報價快取 30 秒、股票清單與已結束月份的歷史快取 1 天、當月歷史快取 30 分鐘，以減少上游請求。即時報價可用 `bypassCache: true` 略過快取。
4. 歷史資料的成交量一律是「股」、成交金額一律是「元」，上市與上櫃單位一致。

## 版本紀錄

完整說明見 [GitHub Releases](https://github.com/twjackysu/TWStockLib/releases)。

| 版本 | 變更 |
|------|------|
| 2.3.0 | 修正多項資料與行為錯誤：上櫃歷史單位、牛熊證過濾、語系相依的解析、取消與逾時、價格訂閱的執行緒安全與生命週期等（見下方升級說明）；AngleSharp 最低版本升至 1.5.0（修補 GHSA-pgww-w46g-26qg） |
| 2.2.1 | 套件內 DLL 的組件版本改為與套件版本一致（2.2.0 的 DLL 仍標示為 2.1.1），功能與 2.2.0 相同 |
| 2.2.0 | 即時報價新增 `bypassCache` 參數，可略過內部 30 秒快取；新增成員皆有預設介面實作，既有實作不需修改 |
| 2.1.1 | 證交所 / 櫃買中心端點改走 https |
| 2.1.0 | 新增 `net10.0` 支援 |
| 2.0.0 | 架構重構，API 有破壞性變更（見下方升級說明） |

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

- `AddStockServices()` → `AddTwStock()`（舊名仍保留但標記 `[Obsolete]`）
- 服務方法改為非同步並回傳 `StockResult<T>`：
  `GetRealtimeQuote` → `GetRealtimeQuoteAsync`、`GetHistoricalData` → `GetHistoricalDataAsync`、
  `GetStockList` → `GetStockListAsync`、`GetAllStockList` → `GetAllStockListAsync`
- 以 `IStockMarketService` 介面注入（取代具體型別 `StockMarketService`）

## 授權

本專案採用 MIT 授權，詳見 [LICENSE](LICENSE)。
