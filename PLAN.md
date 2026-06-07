# TWStockLib — 重構執行計畫

> 搭配 [DESIGN.md](DESIGN.md) 閱讀。本檔是分階段的可勾選清單。
> 決策已定：多框架 `net8.0;net9.0`、導入 `StockResult<T>`、JSON 改用 `System.Text.Json`。

每個 Phase 結束都應可 build + test 通過再進下一階段。

---

## Phase 0 — 工程地基（低風險，先做）

- [ ] 新增 `Directory.Build.props`：
  - [ ] `<TargetFrameworks>net8.0;net9.0</TargetFrameworks>`
  - [ ] `<Nullable>enable</Nullable>`、`<LangVersion>latest</LangVersion>`、`<ImplicitUsings>enable</ImplicitUsings>`
  - [ ] 共用 NuGet metadata：Authors / Company / RepositoryUrl / PackageLicenseExpression / PackageProjectUrl
  - [ ] `IncludeSymbols=true`、`SymbolPackageFormat=snupkg`、`PublishRepositoryUrl=true`、`EmbedUntrackedSources=true`
  - [ ] 加 `Microsoft.SourceLink.GitHub`
- [ ] `.github/workflows/ci.yml`（build + test，push/PR 觸發；setup-dotnet 裝 8.0.x + 9.0.x）
- [ ] `.github/workflows/publish.yml`（tag `v*` 或手動觸發 → pack + push 到 NuGet，用 `secrets.NUGET_API_KEY`）
- [ ] `.sln` → `.slnx`
- [ ] 確認 `GeneratePackageOnBuild` 行為與 README pack 路徑

**驗收**：現有單一專案在 net8.0 + net9.0 都能 build。

---

## Phase 1 — 拆專案 + 補介面（中風險）

- [ ] 建立 `src/TWStockLib.Core` 與 `src/TWStockLib.Twse`，調整方案參考。
- [ ] 搬移檔案：
  - [ ] Models / MarketType / Observer / Cache / `ICacheService` → Core
  - [ ] Factory / Strategy / 解析實作 / API 模型 → Twse
- [ ] 新增 `IStockMarketService`，讓 `StockMarketService` 實作它（**修掉 README 對不上的 bug**）。
- [ ] `TWSEAPIModel` / `TPEXAPIModel` 從公開 Strategy 檔移到 Twse 的 `Internal/`，改 `internal`。
- [ ] DI：`AddStockServices` → `AddTwStock`（保留舊名稱當 `[Obsolete]` 轉呼叫，避免破壞使用者）。

**驗收**：TestExample 改用新 API 後能跑（先不換 Result 也行，分段）。

---

## Phase 2 — 核心重構：Parser/Fetcher 分離 + 每市場 DataSource（重點）

- [ ] 抽 `IStockHttpFetcher` + `TwseHttpFetcher`（集中 `HttpClient`、`Encoding.RegisterProvider`、Big5）。
- [ ] 抽 `IStockParser` + `TwseStockParser`，把 `GetDecimal/GetNullableDecimal/GetUInt32/GetUInt32Array/GetDecimalArray/GetDateTime` 等 helper 全部移入。
- [ ] 拆 `TwseDataFetchStrategy` → `TwseStockDataSource`（TSE）+ `TpexStockDataSource`（OTC），消滅 `if TSE/else OTC`。
- [ ] `StockMarketService` 改用 `IReadOnlyDictionary<MarketType, IStockDataSource>` 路由。
- [ ] 全鏈導入 `CancellationToken`。
- [ ] 導入 `StockResult<T>`，調整所有對外方法簽名與錯誤碼。
- [ ] JSON 由 Newtonsoft → `System.Text.Json`（重寫 msgArray / TPEX table 解析；移除 Newtonsoft.Json 套件參考）。

**驗收**：對 2330(TSE) / 00687B(OTC) 報價、歷史、清單實際打 API 通過。

---

## Phase 3 — 測試

- [ ] `tests/TWStockLib.Twse.Tests`：
  - [ ] 把證交所/櫃買的真實回應存成 fixture（quote JSON、STOCK_DAY JSON、TPEX JSON、ISIN Big5 HTML）。
  - [ ] Parser 對 fixture 斷言輸出（含警證過濾、`-`/空值處理、Big5 解碼）。
- [ ] `tests/TWStockLib.Core.Tests`：
  - [ ] 用假 `IStockDataSource` 測 `StockMarketService` 路由與 `GetAllStockList` 合併邏輯。
  - [ ] `StockResult<T>` 的 Ok/Fail 行為。
  - [ ] Observer 通知邏輯。

**驗收**：`dotnet test` 全綠，CI 上跑得過。

---

## Phase 4 — 收尾與發佈

- [ ] 更新 `README.md`：對齊新 API（`AddTwStock`、`StockResult<T>`、async/ct），修掉 `CreatePriceObserver` 等不存在的範例。
- [ ] `DESIGN.md` 連入 README（設計模式總表就是「高大尚」觀感來源）。
- [ ] 版本號訂為 `1.1.0`（API 有破壞性變更，主版號可考慮 `2.0.0`）。
- [ ] 設定 GitHub repo secret `NUGET_API_KEY`。
- [ ] 打 tag `v1.1.0` → 確認 publish workflow 成功推上 NuGet（Core + Twse 兩個套件）。

**驗收**：`dotnet add package TWStockLib.Twse` 能裝到新版並正常運作。

---

## 風險與注意

- 證交所/櫃買的 HTML/JSON 格式偶爾會變，fixture 測試只保證「解析邏輯正確」，不保證上游不變——CI 不要對真實網路做整合測試（會 flaky）。
- API 破壞性變更：保留 `AddStockServices` 的 `[Obsolete]` 轉呼叫，給既有使用者緩衝。
- `netstandard2.0` 這次不納入（記於決策）；若日後要支援 .NET Framework 使用者，record / collection expression 需做條件編譯。
