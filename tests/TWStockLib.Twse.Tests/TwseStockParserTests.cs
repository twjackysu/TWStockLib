using System.Globalization;

namespace TWStockLib.Twse.Tests;

public class TwseStockParserTests
{
    // ── 即時報價 ──────────────────────────────────────────────────────────────

    private const string QuoteJson = """
    {"msgArray":[{
        "c":"2330","n":"台積電",
        "z":"600.00","tv":"5","v":"20000",
        "a":"601.00_602.00","f":"10_20",
        "b":"599.00_598.00","g":"30_40",
        "o":"595.00","h":"605.00","l":"594.00",
        "y":"596.00","u":"655.00","w":"537.00",
        "tlong":"1699000000000"
    }]}
    """;

    [Fact]
    public void ParseRealtimeQuote_MapsAllFields()
    {
        var quote = TwseStockParser.ParseRealtimeQuote(QuoteJson, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Equal("2330", quote!.Symbol);
        Assert.Equal("台積電", quote.Name);
        Assert.Equal(MarketType.TSE, quote.Market);
        Assert.Equal(600.00m, quote.LastPrice);
        Assert.Equal(596.00m, quote.YesterdayClosingPrice);
        Assert.Equal([new PriceLevel(601.00m, 10), new PriceLevel(602.00m, 20)], quote.Asks);
        Assert.Equal([new PriceLevel(599.00m, 30), new PriceLevel(598.00m, 40)], quote.Bids);
        Assert.Equal(20_000L, quote.TotalVolume);
        Assert.Equal(new DateTimeOffset(2023, 11, 3, 16, 26, 40, TimeSpan.FromHours(8)), quote.SyncTime);
    }

    [Fact]
    public void ParseRealtimeQuote_EmptyMsgArray_ReturnsNull()
    {
        Assert.Null(TwseStockParser.ParseRealtimeQuote("""{"msgArray":[]}""", MarketType.TSE));
    }

    [Fact]
    public void ParseRealtimeQuote_UnknownSymbolOrWrongMarket_ReturnsNull()
    {
        // MIS 對查無代號（tse_ZZZZ）與市場錯誤（otc_2330）都回這筆佔位資料（實際回應節錄）
        var json = """{"msgArray":[{"tv":"-","s":"-","c":"","z":"-"}],"rtcode":"0000","rtmessage":"OK"}""";

        Assert.Null(TwseStockParser.ParseRealtimeQuote(json, MarketType.OTC));
    }

    // 以 ex_ch=tse_2330.tw|tse_9999.tw|otc_2330.tw|tse_2317.tw 查詢的實際回應（節錄欄位）：
    // 每個 ex_ch 依序一筆，查無代號與市場錯誤的那兩筆是佔位資料
    private const string MultiQuoteJson = """
    {"msgArray":[
        {"c":"2330","ch":"2330.tw","ex":"tse","n":"台積電","z":"2385.0000","y":"2420.0000","tlong":"1792650600000"},
        {"tv":"-","s":"-","c":"","z":"-"},
        {"tv":"-","s":"-","c":"","z":"-"},
        {"c":"2317","ch":"2317.tw","ex":"tse","n":"鴻海","z":"245.0000","y":"250.0000","tlong":"1792650600000"}
    ],"rtcode":"0000","rtmessage":"OK"}
    """;

    [Fact]
    public void ParseRealtimeQuotes_ParsesEveryItem_AndSkipsPlaceholders()
    {
        var quotes = TwseStockParser.ParseRealtimeQuotes(MultiQuoteJson, MarketType.TSE);

        Assert.Equal(["2330", "2317"], quotes.Select(q => q.Symbol));
        Assert.Equal([2385.0000m, 245.0000m], quotes.Select(q => q.LastPrice!.Value));
        Assert.Equal("鴻海", quotes[1].Name);
        Assert.All(quotes, q => Assert.Equal(MarketType.TSE, q.Market));
    }

    [Fact]
    public void ParseRealtimeQuotes_OnlyPlaceholders_ReturnsEmpty()
    {
        var json = """{"msgArray":[{"tv":"-","c":"","z":"-"},{"tv":"-","c":"","z":"-"}],"rtcode":"0000","rtmessage":"OK"}""";

        Assert.Empty(TwseStockParser.ParseRealtimeQuotes(json, MarketType.TSE));
    }

    [Fact]
    public void ParseRealtimeQuotes_ARejectedQuery_Throws_InsteadOfReportingEverySymbolAsNotFound()
    {
        // 一次查 200 檔時 MIS 的實際回應：沒有 msgArray
        var json = """{"rtcode":"9999","rtmessage":"參數不足"}""";

        var error = Assert.Throws<HttpRequestException>(() => TwseStockParser.ParseRealtimeQuotes(json, MarketType.TSE));
        Assert.Contains("9999", error.Message);
        Assert.Throws<HttpRequestException>(() => TwseStockParser.ParseRealtimeQuote(json, MarketType.TSE));
    }

    [Fact]
    public void ParseRealtimeQuote_TakesTheFirstRealItem()
    {
        Assert.Equal("2330", TwseStockParser.ParseRealtimeQuote(MultiQuoteJson, MarketType.TSE)!.Symbol);
    }

    [Fact]
    public void ParseRealtimeQuote_DashValues_BecomeNull()
    {
        var json = """{"msgArray":[{"c":"2330","n":"台積電","z":"-","o":"-"}]}""";

        var quote = TwseStockParser.ParseRealtimeQuote(json, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Null(quote!.LastPrice);
        Assert.Null(quote.OpeningPrice);
    }

    [Fact]
    public void ParseRealtimeQuote_MissingReferencePrices_BecomeNullNotZero()
    {
        // 指數沒有漲跌停：u / w 為「-」，不該變成 0 這種看似合法的價格
        var json = """{"msgArray":[{"c":"t00","n":"發行量加權股價指數","z":"23100.12","y":"-","u":"-","w":"-"}]}""";

        var quote = TwseStockParser.ParseRealtimeQuote(json, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Null(quote!.LimitUp);
        Assert.Null(quote.LimitDown);
        Assert.Null(quote.YesterdayClosingPrice);
    }

    [Fact]
    public void ParseRealtimeQuote_AMissingLevel_DoesNotShiftPricesAgainstVolumes()
    {
        // 第二檔價格缺值：其餘檔位的價格與張數仍須依位置配對
        var json = """{"msgArray":[{"c":"2330","n":"台積電","a":"601.00_-_603.00_","f":"10_20_30_"}]}""";

        var quote = TwseStockParser.ParseRealtimeQuote(json, MarketType.TSE);

        Assert.Equal([new PriceLevel(601.00m, 10), new PriceLevel(603.00m, 30)], quote!.Asks);
        Assert.Empty(quote.Bids);
    }

    // ── TSE 歷史（STOCK_DAY）─────────────────────────────────────────────────

    [Fact]
    public void ParseTwseHistory_OkStat_ParsesRowsWithRocDate()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","1,000,000","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"]
        ]}
        """;

        var rows = TwseStockParser.ParseTwseHistory(json);

        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2019, 11, 1), row.Date); // 民國 108 → 西元 2019
        Assert.Equal(1_000_000L, row.TradeVolume);
        Assert.Equal(30.00m, row.OpeningPrice);
        Assert.Equal(30.50m, row.ClosingPrice);
        Assert.Equal(0.50m, row.Change);
        Assert.Equal(500L, row.NumberOfDeals);
    }

    [Fact]
    public void ParseTwseHistory_NonOkStat_ReturnsEmpty()
    {
        Assert.Empty(TwseStockParser.ParseTwseHistory("""{"stat":"很抱歉，沒有符合條件的資料!"}"""));
    }

    [Fact]
    public void ParseTwseHistory_ParsesNumbersTheSameWayInAnyCulture()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","1,000,000","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"]
        ]}
        """;
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // 「.」是千分位、「,」是小數點
        try
        {
            var row = Assert.Single(TwseStockParser.ParseTwseHistory(json));

            Assert.Equal(30.50m, row.ClosingPrice);
            Assert.Equal(30_000_000m, row.TurnoverValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ParseTwseHistory_MalformedRows_AreSkipped_NotFilledWithFakeValues()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","1,000,000","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"],
            ["合計","1,000,000","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"],
            ["108/11/04","N/A","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"],
            ["108/11/05","1,000,000"]
        ]}
        """;

        var row = Assert.Single(TwseStockParser.ParseTwseHistory(json));

        Assert.Equal(new DateOnly(2019, 11, 1), row.Date);
    }

    [Fact]
    public void ParseTwseHistory_NoTradeDay_IsKeptWithoutPrices()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","0","0","--","--","--","--"," 0.00","0"]
        ]}
        """;

        var row = Assert.Single(TwseStockParser.ParseTwseHistory(json));

        Assert.Equal(new DateOnly(2019, 11, 1), row.Date);
        Assert.Null(row.OpeningPrice);
        Assert.Null(row.ClosingPrice);
        Assert.Equal(0L, row.TradeVolume);
    }

    [Fact]
    public void ParseTwseHistory_ExRightsDay_HasNoChange()
    {
        // 除權息當日不比價，漲跌價差欄是「X0.00」
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","1,000,000","30,000,000","30.00","31.00","29.50","30.50","X0.00","500"]
        ]}
        """;

        var row = Assert.Single(TwseStockParser.ParseTwseHistory(json));

        Assert.Null(row.Change);
        Assert.Equal(30.50m, row.ClosingPrice);
    }

    [Fact]
    public void ParsedCollections_AreReadOnly_SoTheyCanBeSharedThroughTheCache()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","1,000,000","30,000,000","30.00","31.00","29.50","30.50","+0.50","500"]
        ]}
        """;
        var quote = TwseStockParser.ParseRealtimeQuote(QuoteJson, MarketType.TSE)!;
        var history = TwseStockParser.ParseTwseHistory(json);
        var list = TwseStockParser.ParseStockList(IsinFixtures.Tse, MarketType.TSE);

        Assert.True(((ICollection<PriceLevel>)quote.Asks).IsReadOnly);
        Assert.True(((ICollection<StockHistory>)history).IsReadOnly);
        Assert.True(((ICollection<KeyValuePair<string, StockData>>)list).IsReadOnly);
    }

    // ── OTC 歷史（TPEX st43）─────────────────────────────────────────────────

    [Fact]
    public void ParseTpexHistory_ConvertsThousandsOfSharesAndDollars_ToMatchTwse()
    {
        // 櫃買 st43 的成交量是「仟股」、成交金額是「仟元」（6510 精測 2025/09/01 的實際回應）
        const string json = """
        {"tables":[{"data":[
            ["114/09/01","1,253","1,521,831","1,225.00","1,245.00","1,175.00","1,220.00","-25.00","2,466"]
        ]}]}
        """;

        var rows = TwseStockParser.ParseTpexHistory(json);

        var row = Assert.Single(rows);
        Assert.Equal(new DateOnly(2025, 9, 1), row.Date);
        Assert.Equal(1_253_000L, row.TradeVolume);
        Assert.Equal(1_521_831_000m, row.TurnoverValue);
        Assert.Equal(1_220m, row.ClosingPrice);
        Assert.Equal(-25m, row.Change);
        Assert.Equal(2_466L, row.NumberOfDeals);
    }

    [Fact]
    public void ParseTpexHistory_NoTables_ReturnsEmpty()
    {
        Assert.Empty(TwseStockParser.ParseTpexHistory("""{"tables":[]}"""));
    }

    // ── 股票清單（ISIN HTML）──────────────────────────────────────────────────

    [Fact]
    public void ParseStockList_ReturnsTheFullList_IncludingWarrantsAndBullBearCertificates()
    {
        // 是否排除權證由資料來源在讀取時依代號過濾，parser 一律回傳完整清單
        var list = TwseStockParser.ParseStockList(IsinFixtures.Tse, MarketType.TSE);

        Assert.Equal(15, list.Count);
        Assert.All(["030001", "03004T", "03726B", "03989C"], symbol => Assert.Contains(symbol, list.Keys));
    }

    [Fact]
    public void ParseStockList_MapsSymbolNameAndMarket()
    {
        var list = TwseStockParser.ParseStockList(IsinFixtures.Tse, MarketType.TSE);

        Assert.Equal(new StockData { Symbol = "2330", Name = "台積電", Market = MarketType.TSE }, list["2330"]);
        Assert.Equal("華德動能-創", list["2237"].Name);
    }

    [Fact]
    public void ParseStockList_Otc()
    {
        var list = TwseStockParser.ParseStockList(IsinFixtures.Otc, MarketType.OTC);

        Assert.Equal(7, list.Count);
        Assert.Equal(new StockData { Symbol = "6510", Name = "精測", Market = MarketType.OTC }, list["6510"]);
    }

    // ── 股票清單（OpenAPI）────────────────────────────────────────────────────

    [Fact]
    public void ParseTwseOpenApiStockList_MapsCodeAndName()
    {
        var list = TwseStockParser.ParseTwseOpenApiStockList(OpenApiFixtures.TwseStockDayAll, MarketType.TSE);

        Assert.Equal(8, list.Count);
        Assert.Equal(new StockData { Symbol = "00631L", Name = "元大台灣50正2", Market = MarketType.TSE }, list["00631L"]);
        Assert.True(((ICollection<KeyValuePair<string, StockData>>)list).IsReadOnly);
    }

    [Fact]
    public void ParseTpexOpenApiStockList_MapsCodeAndName_IncludingWarrants()
    {
        var list = TwseStockParser.ParseTpexOpenApiStockList(OpenApiFixtures.TpexDailyClose, MarketType.OTC);

        Assert.Equal(6, list.Count);
        Assert.Equal(new StockData { Symbol = "6510", Name = "精測", Market = MarketType.OTC }, list["6510"]);
        Assert.Equal("擎亞群益5B售01", list["72479U"].Name);
    }

    [Theory]
    [InlineData(2026, 10, 22, new[] { "030001", "03004T", "03080T", "03726B" })]
    [InlineData(2026, 10, 23, new[] { "030001", "03004T", "03726B" })]          // 03080T 昨天最後交易
    [InlineData(2026, 10, 14, new[] { "030001", "03001T", "03004T", "03080T", "03726B" })]
    public void ParseTwseOpenApiWarrantList_KeepsWarrantsUntilTheirLastTradingDay(int year, int month, int day, string[] expected)
    {
        var list = TwseStockParser.ParseTwseOpenApiWarrantList(OpenApiFixtures.TwseWarrants, MarketType.TSE, new DateOnly(year, month, day));

        Assert.Equal(expected, list.Keys.Order());
    }

    [Fact]
    public void ParseTwseOpenApiWarrantList_AnUnreadableLastTradingDay_IsKept()
    {
        const string json = """[{"權證代號":"030001","權證簡稱":"友達台新6A購01","最後交易日":"N/A"}]""";

        var list = TwseStockParser.ParseTwseOpenApiWarrantList(json, MarketType.TSE, new DateOnly(2026, 10, 22));

        Assert.Equal(["030001"], list.Keys);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"stat":"OK","data":[]}""")]
    public void ParseOpenApiStockLists_AnEmptyOrUnexpectedResponse_Throws_SoItIsNotCachedAsAnEmptyList(string json)
    {
        Assert.Throws<FormatException>(() => TwseStockParser.ParseTwseOpenApiStockList(json, MarketType.TSE));
        Assert.Throws<FormatException>(() => TwseStockParser.ParseTpexOpenApiStockList(json, MarketType.OTC));
    }

    [Fact]
    public void ParseOpenApiStockLists_AnHtmlErrorPage_IsAJsonError()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => TwseStockParser.ParseTwseOpenApiStockList("<html>系統忙碌中</html>", MarketType.TSE));
    }
}
