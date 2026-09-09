using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Providers;

// One provider bar, read out of one JSON object.
//
// Shared by every feed that returns bars, because the adjustment is arithmetic
// on stored values and a second copy of it is a second place the arithmetic
// lives. The historical endpoint and the bulk endpoint return the same session
// object under different roots, so the difference between the two feeds is
// where the objects come from and never what a bar means.
// see: Code owns every number
public static class ProviderBarReader
{
    // The scale the adjustment is carried to. Four places, which is what the
    // provider carries on an adjusted close, so a scaled open is stated no more
    // precisely than the number the factor came from.
    const int Places = 4;

    // A row that is a session, or nothing.
    //
    // The bulk file is the whole exchange rather than the index, so it carries
    // symbols that are listed and did not trade. On the first live night that
    // was 62 rows of 44,362, and `Read` refusing one of them stopped the night
    // for five hundred names on account of a penny stock. The fixture could not
    // have shown this: seven rows were captured by hand and every one of them
    // traded.
    //
    // Only the not-a-session case answers false. A row missing a field, or
    // carrying a date that will not parse, still throws, because a payload that
    // cannot be read must fail rather than answer with fewer bars.
    public static bool TryRead(JsonElement entry, string ticker, out ProviderBar? bar)
    {
        bar = null;

        if (Price(entry, "close", ticker) <= 0)
        {
            return false;
        }

        bar = Read(entry, ticker);

        return true;
    }

    public static ProviderBar Read(JsonElement entry, string ticker)
    {
        // Both closes. The adjusted one is what the store holds
        // (see: The stored series is adjusted); the raw one is the input to the
        // factor and is stored beside it.
        var raw = Price(entry, "close", ticker);
        var adjusted = Price(entry, "adjusted_close", ticker);

        if (raw <= 0)
        {
            throw new FormatException(
                $"{ticker} carries a close of {raw}, and the adjustment factor divides by it. A " +
                "session with no positive close is not a session.");
        }

        // One price set per bar. The provider adjusts the close alone, so the
        // other three are scaled by the same factor. Passing them through
        // unadjusted stores three raw prices beside one adjusted one, which is
        // a bar that could not have traded: 1.2 stored 96 of 756 fixture bars
        // whose close fell outside their own low and high.
        var bar = new ProviderBar(
            Date(entry, "date", ticker),
            Adjust(Price(entry, "open", ticker), adjusted, raw),
            Adjust(Price(entry, "high", ticker), adjusted, raw),
            Adjust(Price(entry, "low", ticker), adjusted, raw),
            adjusted,
            raw,
            Volume(entry, ticker));

        // The guard the code carries, beside the check that reads the store. A
        // bar that could not have traded refuses here rather than being drawn.
        if (bar.Low > bar.Open || bar.Low > bar.Close || bar.High < bar.Open || bar.High < bar.Close)
        {
            throw new FormatException(
                $"{ticker} on {bar.SessionDate:yyyy-MM-dd} adjusts to low {bar.Low}, open {bar.Open}, " +
                $"high {bar.High}, close {bar.Close}, which is a session that could not have traded.");
        }

        return bar;
    }

    // value * adjusted / raw, in that order, so the division happens once at the
    // end rather than on a factor rounded before it is used. Decimal throughout:
    // a price never passes through double, and a ratio of two prices is not a
    // statistic, it is the same price expressed after a corporate action.
    static decimal Adjust(decimal value, decimal adjusted, decimal raw) =>
        adjusted == raw ? value : PriceForm.Round(value * adjusted / raw, Places);

    static DateOnly Date(JsonElement entry, string name, string ticker)
    {
        var text = Text(entry, name, ticker);

        // Exact and invariant, so the parse does not depend on the machine's
        // locale. A day-first string would otherwise be a different date here
        // and a refusal on the runner.
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException($"{ticker} carries a {name} of '{text}', which is not a date in yyyy-MM-dd.");
    }

    // Read as decimal from the text of the number, never through double.
    // GetDecimal on a JSON number does not go through double, and the money rule
    // is that a price is decimal in code from the moment it is read.
    static decimal Price(JsonElement entry, string name, string ticker)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            throw new FormatException($"{ticker} carries no {name}. A session without a price is not a session.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new FormatException(
                $"{ticker} carries a {name} that is not a number this store will accept. Prices are " +
                "decimal in code, and a value that will not parse as one is not a price."),
        };
    }

    static long Volume(JsonElement entry, string ticker) =>
        entry.TryGetProperty("volume", out var value) && value.ValueKind is JsonValueKind.Number
            ? value.GetInt64()
            : throw new FormatException($"{ticker} carries no volume, or one that is not a whole number.");

    static string Text(JsonElement entry, string name, string ticker) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"{ticker} carries no {name}, or one that is not a string.");
}
