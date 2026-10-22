# 版本紀錄

完整說明見 [GitHub Releases](https://github.com/twjackysu/TWStockLib/releases)。

| 版本 | 變更 |
|------|------|
| 3.0.0 | 破壞性變更：不可變 record 模型、`DateOnly`、`QuoteOptions` / `StockListOptions`、以 `WatchAsync` 取代觀察者、命名空間整併、實作類別改為 internal（見 [MIGRATION.md](MIGRATION.md)）。新功能：一次查多檔報價與多檔追蹤、不指定市場自動判斷上市或上櫃、股票清單預設改用 OpenAPI（可選 ISIN）、每主機限流、`TwStockOptions` 可調整快取時間與限流、具名 HttpClient 可自行加上重試、同時發生的相同查詢只向上游要一次、代號格式檢查 |
| 2.3.1 | 查無代號、或代號不屬於指定的市場（例如以 `MarketType.OTC` 查 2330）時，即時報價改回 `NOT_FOUND`；先前回傳欄位全空的成功結果 |
| 2.3.0 | 修正多項資料與行為錯誤：上櫃歷史單位、牛熊證過濾、語系相依的解析、取消與逾時、價格訂閱的執行緒安全與生命週期等；AngleSharp 最低版本升至 1.5.0（修補 GHSA-pgww-w46g-26qg） |
| 2.2.1 | 套件內 DLL 的組件版本改為與套件版本一致（2.2.0 的 DLL 仍標示為 2.1.1），功能與 2.2.0 相同 |
| 2.2.0 | 即時報價新增 `bypassCache` 參數，可略過內部 30 秒快取；新增成員皆有預設介面實作，既有實作不需修改 |
| 2.1.1 | 證交所 / 櫃買中心端點改走 https |
| 2.1.0 | 新增 `net10.0` 支援 |
| 2.0.0 | 架構重構，API 有破壞性變更（見 [MIGRATION.md](MIGRATION.md)） |
