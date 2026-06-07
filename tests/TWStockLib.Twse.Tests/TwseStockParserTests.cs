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

    // ── OTC 歷史（TPEX st43）─────────────────────────────────────────────────

    [Fact]
    public void ParseTpexHistory_ParsesFirstTableRows()
    {
        const string json = """
        {"tables":[{"data":[
            ["112/11/01","2,000","60,000","100.0","101.0","99.0","100.5","0.5","123"]
        ]}]}
        """;

        var rows = _parser.ParseTpexHistory(json);

        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2023, 11, 1), row.Date);
        Assert.Equal(2_000u, row.TradeVolume);
        Assert.Equal(100.5m, row.ClosingPrice);
    }

    [Fact]
    public void ParseTpexHistory_NoTables_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseTpexHistory("""{"tables":[]}"""));
    }

    // ── 股票清單（ISIN HTML）──────────────────────────────────────────────────

    private const string IsinHtml = """
    <html><body><table class="h4"><tbody>
        <tr><td bgcolor="#FAFAD2">2330　台積電</td></tr>
        <tr><td bgcolor="#FAFAD2">081234　大盤購01</td></tr>
    </tbody></table></body></html>
    """;

    [Fact]
    public void ParseStockList_ExcludesWarrants_ByDefault()
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

        Assert.Equal(2, list.Count);
        Assert.Contains("081234", list.Keys);
    }
}
