# TWStockLib

[![NuGet](https://img.shields.io/nuget/v/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TWStockLib.svg)](https://www.nuget.org/packages/TWStockLib)
[![CI](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml/badge.svg)](https://github.com/twjackysu/TWStockLib/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/twjackysu/TWStockLib.svg)](https://github.com/twjackysu/TWStockLib/blob/master/LICENSE)

台灣股市資料的 .NET 類別庫：上市（TSE）與上櫃（OTC）的股票清單、歷史日成交、即時報價與成交價追蹤，
資料來自證交所與櫃買中心。支援 `net8.0` / `net9.0` / `net10.0`。

- 全非同步、可取消，查詢結果統一為 `StockResult<T>`，不必到處 try-catch
- 一次查多檔、不指定市場自動判斷上市或上櫃
- 內建快取與限流，不會讓證交所封鎖你的 IP

## 安裝

```
dotnet add package TWStockLib
```

## 快速入門

```csharp
using Microsoft.Extensions.DependencyInjection;
using TWStockLib;

var services = new ServiceCollection();
services.AddLogging();
services.AddTwStock();
var stock = services.BuildServiceProvider().GetRequiredService<IStockMarketService>();

var result = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);
if (result.IsSuccess)
    Console.WriteLine($"{result.Value.Name} {result.Value.LastPrice}");
else
    Console.WriteLine($"{result.ErrorCode}: {result.ErrorMessage}");
```

ASP.NET Core 中在 `Program.cs` 呼叫 `builder.Services.AddTwStock()`，再以建構式注入 `IStockMarketService`。

## 用法

```csharp
// 即時報價（預設快取 30 秒；需要最新值時傳 QuoteOptions.NoCache）
var quote = await stock.GetRealtimeQuoteAsync("2330", MarketType.TSE);

// 一次查多檔；不確定上市或上櫃時可省略市場
var quotes = await stock.GetRealtimeQuotesAsync(["2330", "6510", "0050"]);

// 歷史日成交
var history = await stock.GetHistoricalDataAsync(
    "2330", MarketType.TSE, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30));

// 股票清單（預設不含權證）
var list = await stock.GetAllStockListAsync();

// 追蹤成交價：先回傳目前報價，之後只在價格變動時回傳
await foreach (var q in stock.WatchAsync(["2330", "6510"], ct: stoppingToken))
    Console.WriteLine($"{q.Symbol} {q.LastPrice}");
```

快取時間與限流可以調整：

```csharp
services.AddTwStock(options => options.QuoteCacheDuration = TimeSpan.FromSeconds(10));
```

## 文件

- [使用說明](https://github.com/twjackysu/TWStockLib/blob/master/docs/USAGE.md)：各項查詢的行為、設定、限流、重試、擴充與錯誤碼
- [升級指南](https://github.com/twjackysu/TWStockLib/blob/master/MIGRATION.md)
- [版本紀錄](https://github.com/twjackysu/TWStockLib/blob/master/CHANGELOG.md)
- [設計文件](https://github.com/twjackysu/TWStockLib/blob/master/DESIGN.md)

## 授權

MIT
