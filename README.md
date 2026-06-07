# TWStockLib

TWStockLib 是一個用於獲取台灣股市資料的 .NET 類別庫，提供股票清單、歷史數據與即時報價，並支援價格變化的觀察者模式。

- 全非同步、可取消（`async` + `CancellationToken`）
- 統一以 `StockResult<T>` 回傳成功 / 失敗，呼叫端免到處 try-catch
- 每個市場一個資料來源，新增市場零修改現有程式碼（OCP）
- 純函式解析層（與 IO 分離），可離線單元測試
- 支援 `net8.0` / `net9.0`

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

## 支援的市場

- TSE（台灣證券交易所，上市）
- OTC（櫃買中心，上櫃）

## 注意事項

1. 所有資料皆從網路即時獲取，需要連線。
2. 歷史數據以月為單位查詢，跨月會自動合併。
3. 即時報價快取 30 秒、股票清單與歷史快取 1 天，以減少上游請求。

## 從 1.0.x 升級

2.0 版為架構重構，API 有破壞性變更：

- `AddStockServices()` → `AddTwStock()`（舊名仍保留但標記 `[Obsolete]`）
- 服務方法改為非同步並回傳 `StockResult<T>`：
  `GetRealtimeQuote` → `GetRealtimeQuoteAsync`、`GetHistoricalData` → `GetHistoricalDataAsync`、
  `GetStockList` → `GetStockListAsync`、`GetAllStockList` → `GetAllStockListAsync`
- 以 `IStockMarketService` 介面注入（取代具體型別 `StockMarketService`）

## 授權

本專案採用 MIT 授權，詳見 [LICENSE](LICENSE)。
