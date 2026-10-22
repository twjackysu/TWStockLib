using System.Globalization;

namespace TWStockLib.Twse.Tests;

public class TwseStockParserTests
{
    private readonly TwseStockParser _parser = new();

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
        var quote = _parser.ParseRealtimeQuote(QuoteJson, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Equal("2330", quote!.Symbol);
        Assert.Equal("台積電", quote.Name);
        Assert.Equal(MarketType.TSE, quote.Market);
        Assert.Equal(600.00m, quote.LastPrice);
        Assert.Equal(596.00m, quote.YesterdayClosingPrice);
        Assert.Equal(new[] { 601.00m, 602.00m }, quote.Top5SellPrice);
        Assert.Equal(new uint[] { 10, 20 }, quote.Top5SellVolume);
        Assert.Equal(new[] { 599.00m, 598.00m }, quote.Top5BuyPrice);
    }

    [Fact]
    public void ParseRealtimeQuote_EmptyMsgArray_ReturnsNull()
    {
        Assert.Null(_parser.ParseRealtimeQuote("""{"msgArray":[]}""", MarketType.TSE));
    }

    [Fact]
    public void ParseRealtimeQuote_DashValues_BecomeNull()
    {
        var json = """{"msgArray":[{"c":"2330","n":"台積電","z":"-","o":"-"}]}""";

        var quote = _parser.ParseRealtimeQuote(json, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Null(quote!.LastPrice);
        Assert.Null(quote.OpeningPrice);
    }

    [Fact]
    public void ParseRealtimeQuote_MissingReferencePrices_BecomeNullNotZero()
    {
        // 指數沒有漲跌停：u / w 為「-」，不該變成 0 這種看似合法的價格
        var json = """{"msgArray":[{"c":"t00","n":"發行量加權股價指數","z":"23100.12","y":"-","u":"-","w":"-"}]}""";

        var quote = _parser.ParseRealtimeQuote(json, MarketType.TSE);

        Assert.NotNull(quote);
        Assert.Null(quote!.LimitUp);
        Assert.Null(quote.LimitDown);
        Assert.Null(quote.YesterdayClosingPrice);
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

        var rows = _parser.ParseTwseHistory(json);

        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2019, 11, 1), row.Date); // 民國 108 → 西元 2019
        Assert.Equal(1_000_000u, row.TradeVolume);
        Assert.Equal(30.00m, row.OpeningPrice);
        Assert.Equal(30.50m, row.ClosingPrice);
        Assert.Equal(500u, row.NumberOfDeals);
    }

    [Fact]
    public void ParseTwseHistory_NonOkStat_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseTwseHistory("""{"stat":"很抱歉，沒有符合條件的資料!"}"""));
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
            var row = Assert.Single(_parser.ParseTwseHistory(json));

            Assert.Equal(30.50m, row.ClosingPrice);
            Assert.Equal(30_000_000m, row.TurnOverInValue);
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

        var row = Assert.Single(_parser.ParseTwseHistory(json));

        Assert.Equal(new DateTime(2019, 11, 1), row.Date);
    }

    [Fact]
    public void ParseTwseHistory_NoTradeDay_IsKeptWithZeroPrices()
    {
        const string json = """
        {"stat":"OK","data":[
            ["108/11/01","0","0","--","--","--","--"," 0.00","0"]
        ]}
        """;

        var row = Assert.Single(_parser.ParseTwseHistory(json));

        Assert.Equal(new DateTime(2019, 11, 1), row.Date);
        Assert.Equal(0m, row.ClosingPrice);
        Assert.Equal(0u, row.TradeVolume);
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

        var rows = _parser.ParseTpexHistory(json);

        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2025, 9, 1), row.Date);
        Assert.Equal(1_253_000u, row.TradeVolume);
        Assert.Equal(1_521_831_000m, row.TurnOverInValue);
        Assert.Equal(1_220m, row.ClosingPrice);
        Assert.Equal(2_466u, row.NumberOfDeals);
    }

    [Fact]
    public void ParseTpexHistory_NoTables_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseTpexHistory("""{"tables":[]}"""));
    }

    // ── 股票清單（ISIN HTML）──────────────────────────────────────────────────

    // 欄位同 ISIN 實際頁面：代號及名稱、ISIN、上市日、市場別、產業別、CFICode、備註
    private const string IsinHtml = """
    <html><body><table class="h4"><tbody>
        <tr><td bgcolor="#FAFAD2" colspan="7"><B> 股票 </B></td></tr>
        <tr><td bgcolor="#FAFAD2">2330　台積電</td><td bgcolor="#FAFAD2">TW0002330008</td><td bgcolor="#FAFAD2">1994/09/05</td><td bgcolor="#FAFAD2">上市</td><td bgcolor="#FAFAD2">半導體業</td><td bgcolor="#FAFAD2">ESVUFR</td><td bgcolor="#FAFAD2"></td></tr>
        <tr><td bgcolor="#FAFAD2" colspan="7"><B> 上市認購(售)權證 </B></td></tr>
        <tr><td bgcolor="#FAFAD2">0050　中碳凱基64購01</td><td bgcolor="#FAFAD2">TW26Z0300502</td><td bgcolor="#FAFAD2">2026/10/05</td><td bgcolor="#FAFAD2">上市</td><td bgcolor="#FAFAD2"></td><td bgcolor="#FAFAD2">RWSCCA</td><td bgcolor="#FAFAD2"></td></tr>
        <tr><td bgcolor="#FAFAD2">03989C　臺股指凱基5A牛04</td><td bgcolor="#FAFAD2">TW25Z03989C5</td><td bgcolor="#FAFAD2">2025/09/26</td><td bgcolor="#FAFAD2">上市</td><td bgcolor="#FAFAD2"></td><td bgcolor="#FAFAD2">RWICCE</td><td bgcolor="#FAFAD2"></td></tr>
    </tbody></table></body></html>
    """;

    [Fact]
    public void ParseStockList_ExcludesWarrants_IncludingBullBearCertificates_ByDefault()
    {
        var list = _parser.ParseStockList(IsinHtml, MarketType.TSE, includeWarrant: false);

        var entry = Assert.Single(list);
        Assert.Equal("2330", entry.Key);
        Assert.Equal("台積電", entry.Value.Name);
        Assert.Equal(MarketType.TSE, entry.Value.Market);
    }

    [Fact]
    public void ParseStockList_IncludesWarrants_WhenRequested()
    {
        var list = _parser.ParseStockList(IsinHtml, MarketType.TSE, includeWarrant: true);

        Assert.Equal(3, list.Count);
        Assert.Contains("0050", list.Keys);
        Assert.Contains("03989C", list.Keys);
    }
}
