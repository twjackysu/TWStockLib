using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TWStockLib.Twse.Parsers;

/// <summary>
/// 將證交所家族端點的原始回應字串轉成強型別模型。純函式、不碰 IO，可餵固定 fixture 單元測試。
/// 回傳的集合都是唯讀的，可以直接放進快取共用。
/// </summary>
internal static class TwseStockParser
{
    /// <summary>櫃買 st43 的成交量單位是「仟股」、成交金額是「仟元」；證交所 STOCK_DAY 是「股」、「元」。</summary>
    private const int TpexUnitScale = 1000;

    // ISIN 表格欄位依序為：代號及名稱、ISIN、上市日、市場別、產業別、CFICode、備註
    private const int IsinCodeColumn = 1;
    private const int IsinListedDateColumn = 2;
    private const int IsinIndustryColumn = 4;
    private const int IsinCfiCodeColumn = 5;

    private static readonly TimeSpan TaiwanUtcOffset = TimeSpan.FromHours(8);

    public static StockQuote? ParseRealtimeQuote(string json, MarketType market)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("msgArray", out var msgArray)
            || msgArray.ValueKind != JsonValueKind.Array
            || msgArray.GetArrayLength() == 0)
        {
            return null;
        }

        // 查無此代號、或代號不屬於這個市場（例如以上櫃查 2330）時，上游仍回一筆代號為空的佔位資料
        var item = msgArray[0];
        if (string.IsNullOrEmpty(GetString(item, "c")))
            return null;

        return new StockQuote
        {
            Symbol = GetString(item, "c"),
            Market = market,
            Name = GetString(item, "n"),
            LastPrice = GetNullableDecimal(item, "z"),
            LastVolume = GetNullableInt64(item, "tv"),
            TotalVolume = GetNullableInt64(item, "v"),
            Asks = GetPriceLevels(item, priceKey: "a", volumeKey: "f"),
            Bids = GetPriceLevels(item, priceKey: "b", volumeKey: "g"),
            SyncTime = GetUnixTime(item, "tlong"),
            HighestPrice = GetNullableDecimal(item, "h"),
            LowestPrice = GetNullableDecimal(item, "l"),
            OpeningPrice = GetNullableDecimal(item, "o"),
            YesterdayClosingPrice = GetNullableDecimal(item, "y"),
            LimitUp = GetNullableDecimal(item, "u"),
            LimitDown = GetNullableDecimal(item, "w"),
        };
    }

    public static IReadOnlyList<StockHistory> ParseTwseHistory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK")
            return [];
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return MapRows(data, unitScale: 1);
    }

    public static IReadOnlyList<StockHistory> ParseTpexHistory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tables", out var tables)
            || tables.ValueKind != JsonValueKind.Array
            || tables.GetArrayLength() == 0)
        {
            return [];
        }

        var firstTable = tables[0];
        if (!firstTable.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return MapRows(data, TpexUnitScale);
    }

    /// <summary>解析完整清單（含權證），是否排除權證由呼叫端依 <see cref="StockData.SecurityType"/> 過濾。</summary>
    public static IReadOnlyDictionary<string, StockData> ParseStockList(string html, MarketType market)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        // 每列第一格是「代號　名稱」；跨 7 欄的是「股票」、「ETF」這類分段標題
        var elements = doc.QuerySelectorAll(
            "body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");

        var result = new Dictionary<string, StockData>();
        foreach (var el in elements)
        {
            if (TryMapIsinRow(el, market, out var stock))
                result[stock.Symbol] = stock;
        }

        return result.ToFrozenDictionary();
    }

    private static bool TryMapIsinRow(IElement firstCell, MarketType market, [NotNullWhen(true)] out StockData? stock)
    {
        stock = null;
        var parts = firstCell.TextContent.Split('　', 2); // 全形空白
        if (parts.Length < 2)
            return false;

        var cells = firstCell.ParentElement?.Children;
        string Cell(int column) => cells is not null && cells.Length > column ? cells[column].TextContent.Trim() : "";
        var cfiCode = Cell(IsinCfiCodeColumn);

        stock = new StockData
        {
            Symbol = parts[0].Trim(),
            Name = parts[1].Trim(),
            Market = market,
            SecurityType = ToSecurityType(cfiCode),
            Industry = NullIfEmpty(Cell(IsinIndustryColumn)),
            Isin = NullIfEmpty(Cell(IsinCodeColumn)),
            ListedDate = DateOnly.TryParseExact(
                Cell(IsinListedDateColumn), "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var listed)
                ? listed
                : null,
            CfiCode = NullIfEmpty(cfiCode),
        };
        return true;
    }

    /// <summary>
    /// 依 CFICode（ISO 10962）的前兩碼判斷證券類型。對照取自證交所與櫃買 ISIN 頁面實際使用的代碼：
    /// 「RW」開頭即權證，認購、認售與牛熊證都在內——名稱字尾（購 / 售）無法涵蓋牛熊證（如「臺股指凱基5A牛04」）；
    /// 「CM」在標準中是「其他集合投資工具」，兩個市場目前只有 ETN 使用（<c>CMXXXU</c>）。
    /// </summary>
    internal static SecurityType ToSecurityType(string cfiCode)
    {
        if (cfiCode.Length < 2)
            return SecurityType.Unknown;

        return cfiCode[..2] switch
        {
            "ES" => SecurityType.CommonStock,
            "EP" or "EF" => SecurityType.PreferredStock,
            "ED" => SecurityType.DepositaryReceipt,
            "CE" => SecurityType.Etf,
            "CM" => SecurityType.Etn,
            "CB" or "DA" => SecurityType.BeneficiarySecurity,
            "RW" => SecurityType.Warrant,
            _ when cfiCode[0] == 'D' => SecurityType.Bond,
            _ => SecurityType.Other,
        };
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    #region History Rows

    private static IReadOnlyList<StockHistory> MapRows(JsonElement dataArray, int unitScale)
    {
        var result = new List<StockHistory>(dataArray.GetArrayLength());
        foreach (var row in dataArray.EnumerateArray())
        {
            if (TryMapRow(row, unitScale, out var history))
                result.Add(history);
        }
        return result.AsReadOnly();
    }

    /// <summary>
    /// 欄位依序為：日期、成交量、成交金額、開、高、低、收、漲跌價差、成交筆數。
    /// 成交量與金額乘上 <paramref name="unitScale"/> 換算成「股」、「元」。
    /// 日期與數量欄格式不符就略過整列，不以預設值捏造資料；無成交的「--」價格為 <c>null</c>。
    /// </summary>
    private static bool TryMapRow(JsonElement row, int unitScale, [NotNullWhen(true)] out StockHistory? history)
    {
        history = null;
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 9)
            return false;

        var cells = row.EnumerateArray().Select(CellText).ToArray();
        if (!TryParseRocDate(cells[0], out var date)
            || !TryParseScaledInt64(cells[1], unitScale, out var volume)
            || !TryParseHistoryDecimal(cells[2], out var turnover)
            || !TryParseHistoryPrice(cells[3], out var open)
            || !TryParseHistoryPrice(cells[4], out var high)
            || !TryParseHistoryPrice(cells[5], out var low)
            || !TryParseHistoryPrice(cells[6], out var close)
            || !TryParseScaledInt64(cells[8], 1, out var deals))
        {
            return false;
        }

        history = new StockHistory
        {
            Date = date,
            TradeVolume = volume,
            TurnoverValue = turnover * unitScale,
            OpeningPrice = open,
            HighestPrice = high,
            LowestPrice = low,
            ClosingPrice = close,
            // 除權息等不比價的日子是「X0.00」這類非數字，視為沒有漲跌價差
            Change = decimal.TryParse(cells[7], NumberStyles.Number, CultureInfo.InvariantCulture, out var change)
                ? change
                : null,
            NumberOfDeals = deals,
        };
        return true;
    }

    private static string CellText(JsonElement cell)
        => cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? string.Empty : cell.ToString();

    /// <summary>解析「114/09/01」（民國年）或「2025-09-01」（西元年）。</summary>
    private static bool TryParseRocDate(string text, out DateOnly date)
    {
        date = default;
        var parts = text.Replace("＊", "").Trim().Split('/', '-');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        if (year < 1000)
            year += 1911; // 民國年

        if (year > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>無成交的「--」為 <c>null</c>；其他非數字視為格式不符。</summary>
    private static bool TryParseHistoryPrice(string text, out decimal? value)
    {
        value = null;
        if (IsBlank(text.Trim()))
            return true;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryParseHistoryDecimal(string text, out decimal value)
    {
        value = 0m;
        return IsBlank(text.Trim())
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseScaledInt64(string text, int scale, out long value)
    {
        value = 0;
        if (IsBlank(text.Trim()))
            return true;
        if (!long.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var raw)
            || raw < 0
            || raw > long.MaxValue / scale)
        {
            return false;
        }

        value = raw * scale;
        return true;
    }

    #endregion

    #region JSON Helpers

    private static string GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var p) ? p.GetString() ?? string.Empty : string.Empty;

    private static bool IsBlank(string? v) => string.IsNullOrEmpty(v) || v is "-" or "--";

    private static decimal? GetNullableDecimal(JsonElement obj, string key)
        => ParseNullableDecimal(GetString(obj, key));

    private static decimal? ParseNullableDecimal(string v)
    {
        if (IsBlank(v)) return null;
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static long? GetNullableInt64(JsonElement obj, string key)
        => ParseNullableInt64(GetString(obj, key));

    private static long? ParseNullableInt64(string v)
    {
        if (IsBlank(v)) return null;
        return long.TryParse(v, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var l)
            ? l
            : null;
    }

    private static DateTimeOffset? GetUnixTime(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (IsBlank(v)) return null;
        return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TaiwanUtcOffset)
            : null;
    }

    /// <summary>
    /// 五檔的價格與張數分別放在兩個以「_」分隔的欄位（結尾多一個「_」），依位置配對；
    /// 任一邊缺值或無法解析的檔位略過，避免價格與張數錯位。
    /// </summary>
    private static IReadOnlyList<PriceLevel> GetPriceLevels(JsonElement obj, string priceKey, string volumeKey)
    {
        var prices = GetString(obj, priceKey).Split('_');
        var volumes = GetString(obj, volumeKey).Split('_');
        var count = Math.Min(prices.Length, volumes.Length);

        var levels = new List<PriceLevel>(count);
        for (var i = 0; i < count; i++)
        {
            if (ParseNullableDecimal(prices[i]) is { } price && ParseNullableInt64(volumes[i]) is { } volume)
                levels.Add(new PriceLevel(price, volume));
        }
        return levels.AsReadOnly();
    }

    #endregion
}
