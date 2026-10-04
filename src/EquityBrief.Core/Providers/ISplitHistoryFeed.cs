using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One split as the provider files it: the first session trading on the new basis, and the shares one block of old
// shares became, as new over old.
public sealed record SplitAnswer(DateOnly ExDate, decimal NewShares, decimal OldShares);

// One name's splits, in one request a name.
//
// Asked by the history pull on the operator's command and by no night, through the per-name splits endpoint at its
// weight of one. The night reads the day's splits for the whole exchange in one request instead, and has no use for
// a name's history of them.
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public interface ISplitHistoryFeed
{
    int Requests { get; }

    Task<IReadOnlyList<SplitAnswer>> SplitsAsync(string ticker, DateOnly from, CancellationToken cancellation = default);
}

// The splits answer, read.
//
// Written against a captured answer holding six splits, a three for two among them. Each ratio is new over old in
// one string, as the bulk answer the night reads sends it.
public static class SplitAnswers
{
    public static IReadOnlyList<SplitAnswer> Parse(string json, string ticker)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The splits answer for {ticker} is not an array of splits, so it cannot be read, and reading it as none "
                + "would value the company across a split it made as if it had made none.");
        }

        return
        [
            .. document.RootElement.EnumerateArray()
                .Select(entry => Read(entry, ticker))
                .OrderBy(split => split.ExDate),
        ];
    }

    static SplitAnswer Read(JsonElement entry, string ticker)
    {
        var date = entry.ValueKind is JsonValueKind.Object && entry.TryGetProperty("date", out var on) && on.ValueKind is JsonValueKind.String
            ? on.GetString()
            : null;
        var ratio = entry.ValueKind is JsonValueKind.Object && entry.TryGetProperty("split", out var split) && split.ValueKind is JsonValueKind.String
            ? split.GetString()?.Split('/')
            : null;

        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exDate)
            && ratio is [var made, var from]
            && decimal.TryParse(made, NumberStyles.Float, CultureInfo.InvariantCulture, out var newShares)
            && decimal.TryParse(from, NumberStyles.Float, CultureInfo.InvariantCulture, out var oldShares)
            && newShares > 0m
            && oldShares > 0m
                ? new SplitAnswer(exDate, newShares, oldShares)
                : throw new FormatException(
                    $"A split in the answer for {ticker} carries no date or no ratio of two positive numbers, so the "
                    + "basis it moved the shares to cannot be read.");
    }
}
