using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TWStockLib.Models;

namespace TWStockLib.Twse.Parsers;

/// <summary>
/// 將證交所家族端點的原始回應字串轉成強型別模型。純函式、不碰 IO，可餵固定 fixture 單元測試。
/// </summary>
internal static class TwseStockParser
{
    /// <summary>櫃買 st43 的成交量單位是「仟股」、成交金額是「仟元」；證交所 STOCK_DAY 是「股」、「元」。</summary>
    private const uint TpexUnitScale = 1000;

    /// <summary>ISIN 表格欄位依序為：代號及名稱、ISIN、上市日、市場別、產業別、CFICode、備註。</summary>
    private const int IsinCfiCodeColumn = 5;

    public static StockQuote? ParseRealtimeQuote(string json, MarketType market)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("msgArray", out var msgArray)
            || msgArray.ValueKind != JsonValueKind.Array
            || msgArray.GetArrayLength() == 0)
        {
            return null;
        }

        var item = msgArray[0];
        return new StockQuote
        {
            Symbol = GetString(item, "c"),
            Market = market,
            Name = GetString(item, "n"),
            LastPrice = GetNullableDecimal(item, "z"),
            LastVolume = GetNullableUInt32(item, "tv"),
            TotalVolume = GetNullableUInt32(item, "v"),
            Top5SellPrice = GetDecimalArray(item, "a"),
            Top5SellVolume = GetUInt32Array(item, "f"),
            Top5BuyPrice = GetDecimalArray(item, "b"),
            Top5BuyVolume = GetUInt32Array(item, "g"),
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

    public static IReadOnlyDictionary<string, StockData> ParseStockList(string html, MarketType market, bool includeWarrant)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var elements = doc.QuerySelectorAll(
            "body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");

        var result = new Dictionary<string, StockData>();
        foreach (var el in elements)
        {
            if (!includeWarrant && IsWarrant(el))
                continue;

            var parts = el.TextContent.Split('　'); // 全形空白
            if (parts.Length < 2)
                continue;

            var symbol = parts[0];
            result[symbol] = new StockData { Symbol = symbol, Name = parts[1], Market = market };
        }

        return result;
    }

    /// <summary>
    /// 以該列的 CFICode 判斷是否為權證：ISO 10962 的「RW」開頭即權證，認購、認售與牛熊證都在內。
    /// 名稱字尾（購 / 售）無法涵蓋牛熊證（如「臺股指凱基5A牛04」）。
    /// </summary>
    private static bool IsWarrant(IElement firstCell)
    {
        var cells = firstCell.ParentElement?.Children;
        return cells is not null
            && cells.Length > IsinCfiCodeColumn
            && cells[IsinCfiCodeColumn].TextContent.Trim().StartsWith("RW", StringComparison.Ordinal);
    }

    #region History Rows

    private static IReadOnlyList<StockHistory> MapRows(JsonElement dataArray, uint unitScale)
    {
        var result = new List<StockHistory>(dataArray.GetArrayLength());
        foreach (var row in dataArray.EnumerateArray())
        {
            if (TryMapRow(row, unitScale, out var history))
                result.Add(history);
        }
        return result;
    }

    /// <summary>
    /// 欄位依序為：日期、成交量、成交金額、開、高、低、收、漲跌價差、成交筆數。
    /// 成交量與金額乘上 <paramref name="unitScale"/> 換算成「股」、「元」。
    /// 任一欄格式不符就略過整列，不以預設值捏造資料；無成交的「--」視為 0。
    /// </summary>
    private static bool TryMapRow(JsonElement row, uint unitScale, [NotNullWhen(true)] out StockHistory? history)
    {
        history = null;
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 9)
            return false;

        var cells = row.EnumerateArray().Select(CellText).ToArray();
        if (!TryParseRocDate(cells[0], out var date)
            || !TryParseScaledUInt32(cells[1], unitScale, out var volume)
            || !TryParseHistoryDecimal(cells[2], out var turnover)
            || !TryParseHistoryDecimal(cells[3], out var open)
            || !TryParseHistoryDecimal(cells[4], out var high)
            || !TryParseHistoryDecimal(cells[5], out var low)
            || !TryParseHistoryDecimal(cells[6], out var close)
            || !TryParseScaledUInt32(cells[8], 1, out var deals))
        {
            return false;
        }

        history = new StockHistory
        {
            Date = date,
            TradeVolume = volume,
            TurnOverInValue = turnover * unitScale,
            OpeningPrice = open,
            HighestPrice = high,
            LowestPrice = low,
            ClosingPrice = close,
            DailyPricing = cells[7],
            NumberOfDeals = deals,
        };
        return true;
    }

    private static string CellText(JsonElement cell)
        => cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? string.Empty : cell.ToString();

    /// <summary>解析「114/09/01」（民國年）或「2025-09-01」（西元年）。</summary>
    private static bool TryParseRocDate(string text, out DateTime date)
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

        date = new DateTime(year, month, day);
        return true;
    }

    private static bool TryParseHistoryDecimal(string text, out decimal value)
    {
        value = 0m;
        return IsBlank(text.Trim())
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseScaledUInt32(string text, uint scale, out uint value)
    {
        value = 0;
        if (IsBlank(text.Trim()))
            return true;
        if (!ulong.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var raw)
            || raw > uint.MaxValue / scale)
        {
            return false;
        }

        value = (uint)(raw * scale);
        return true;
    }

    #endregion

    #region JSON Helpers

    private static string GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var p) ? p.GetString() ?? string.Empty : string.Empty;

    private static bool IsBlank(string? v) => string.IsNullOrEmpty(v) || v is "-" or "--";

    private static decimal? GetNullableDecimal(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (IsBlank(v)) return null;
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static uint? GetNullableUInt32(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (IsBlank(v)) return null;
        return uint.TryParse(v.Replace(",", ""), out var u) ? u : null;
    }

    private static DateTime? GetUnixTime(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (IsBlank(v)) return null;
        return long.TryParse(v, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime
            : null;
    }

    private static decimal[] GetDecimalArray(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (string.IsNullOrEmpty(v)) return [];
        return v.Split('_')
            .Where(x => !IsBlank(x))
            .Select(x => decimal.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m)
            .ToArray();
    }

    private static uint[] GetUInt32Array(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (string.IsNullOrEmpty(v)) return [];
        return v.Split('_')
            .Where(x => !IsBlank(x))
            .Select(x => uint.TryParse(x.Replace(",", ""), out var u) ? u : 0u)
            .ToArray();
    }

    #endregion
}
