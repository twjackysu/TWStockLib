using System.Globalization;
using System.Text.Json;
using AngleSharp.Html.Parser;
using TWStockLib.Models;

namespace TWStockLib.Twse.Parsers;

/// <inheritdoc cref="IStockParser" />
public sealed class TwseStockParser : IStockParser
{
    public StockQuote? ParseRealtimeQuote(string json, MarketType market)
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
            YesterdayClosingPrice = GetDecimal(item, "y"),
            LimitUp = GetDecimal(item, "u"),
            LimitDown = GetDecimal(item, "w"),
        };
    }

    public IReadOnlyList<StockHistory> ParseTwseHistory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK")
            return [];
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return MapRows(data);
    }

    public IReadOnlyList<StockHistory> ParseTpexHistory(string json)
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

        return MapRows(data);
    }

    public IReadOnlyDictionary<string, StockData> ParseStockList(string html, MarketType market, bool includeWarrant)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var elements = doc.QuerySelectorAll(
            "body > table.h4 > tbody > tr > td:nth-child(1)[bgcolor='#FAFAD2']:not([colspan='7'])");

        var result = new Dictionary<string, StockData>();
        foreach (var el in elements)
        {
            var text = el.TextContent;
            if (text.Length < 3)
                continue;

            // 過濾權證（倒數第 3 個字為「購」或「售」）
            if (!includeWarrant)
            {
                var marker = text[text.Length - 3];
                if (marker == '購' || marker == '售')
                    continue;
            }

            var parts = text.Split('　'); // 全形空白
            if (parts.Length < 2)
                continue;

            var symbol = parts[0];
            result[symbol] = new StockData { Symbol = symbol, Name = parts[1], Market = market };
        }

        return result;
    }

    private static IReadOnlyList<StockHistory> MapRows(JsonElement dataArray)
    {
        var result = new List<StockHistory>(dataArray.GetArrayLength());
        foreach (var row in dataArray.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            var cells = new List<string>(row.GetArrayLength());
            foreach (var cell in row.EnumerateArray())
                cells.Add(cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? string.Empty : cell.ToString());

            result.Add(new StockHistory(cells));
        }
        return result;
    }

    #region JSON Helpers

    private static string GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var p) ? p.GetString() ?? string.Empty : string.Empty;

    private static bool IsBlank(string? v) => string.IsNullOrEmpty(v) || v == "-";

    private static decimal? GetNullableDecimal(JsonElement obj, string key)
    {
        var v = GetString(obj, key);
        if (IsBlank(v)) return null;
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static decimal GetDecimal(JsonElement obj, string key)
        => GetNullableDecimal(obj, key) ?? 0m;

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
