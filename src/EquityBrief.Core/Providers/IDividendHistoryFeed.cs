using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One name's dividends from a date, from the provider's per-name dividends endpoint, one request a name.
//
// Asked by the dividends history run alone, on the operator's command and never by a night: the night keeps the
// dividends its bulk answer carries for each session, and this fills the years before the first night that kept one.
// see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
public interface IDividendHistoryFeed
{
    Task<IReadOnlyList<DividendPaid>> DividendsAsync(string ticker, DateOnly from, CancellationToken cancellation = default);

    int Requests { get; }
}

// The per-name dividends answer: an array of dividends, each its ex-dividend date under `date`, its amount as restated for
// splits on the day it was asked under `value` and as paid under `unadjustedValue`, each a number or a figure in a string,
// and its declaration, record and payment dates, its period and its currency, each null where the provider files none.
// An answer that is no array, or a dividend with no date or no amount, cannot be read and is refused.
public static class DividendAnswers
{
    public static IReadOnlyList<DividendPaid> Parse(string json, string ticker)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The dividends answer for {ticker} is not an array of dividends, so it cannot be read, and reading it as none "
                + "would draw a company that paid as one that never did.");
        }

        return
        [
            .. document.RootElement.EnumerateArray()
                .Select(entry => Read(entry, ticker))
                .OrderBy(paid => paid.ExDate),
        ];
    }

    static DividendPaid Read(JsonElement entry, string ticker)
    {
        if (entry.ValueKind is not JsonValueKind.Object
            || Day(Text(entry, "date")) is not { } exDate
            || Figure(entry, "value") is not { } amount)
        {
            throw new FormatException(
                $"A dividend in the answer for {ticker} carries no ex-dividend date or no amount, so what it paid and when cannot be read.");
        }

        return new DividendPaid(
            ticker,
            exDate,
            amount,
            Figure(entry, "unadjustedValue"),
            Day(Text(entry, "declarationDate")),
            Day(Text(entry, "recordDate")),
            Day(Text(entry, "paymentDate")),
            Text(entry, "period"),
            Text(entry, "currency"));
    }

    static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    static decimal? Figure(JsonElement entry, string name) =>
        !entry.TryGetProperty(name, out var value)
            ? null
            : value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : null,
                JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                _ => null,
            };

    static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
