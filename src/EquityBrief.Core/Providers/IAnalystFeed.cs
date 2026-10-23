using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One month's strong buy to strong sell counts as Yahoo files them, under its own label for the month: "0m" the month
// of the answer, "-1m" the one before it, and so on.
public sealed record YahooMonth(string Period, int StrongBuy, int Buy, int Hold, int Sell, int StrongSell);

// One firm's rating action as Yahoo files it: when, the firm, its action's word, the grade before and after, and the
// target before and after with Yahoo's word for the move, a grade or a target the firm stated none of none.
public sealed record YahooAction(
    DateTimeOffset GradedAt,
    string Firm,
    string Action,
    string? FromGrade,
    string? ToGrade,
    decimal? PriorTarget,
    decimal? Target,
    string? TargetAction);

// Yahoo's answer about one stock's analysts: the currency its prices are in, the low, mean, median and high targets with
// how many analysts set them, Yahoo's word for their recommendation, the counts of each month it files and every firm's
// rating action, newest first, each as Yahoo sent it.
public sealed record YahooAnalystAnswer(
    string? Currency,
    decimal? TargetLow,
    decimal? TargetMean,
    decimal? TargetMedian,
    decimal? TargetHigh,
    int? Analysts,
    string? Recommendation,
    IReadOnlyList<YahooMonth> Months,
    IReadOnlyList<YahooAction> Actions);

// Yahoo's quote summary of one stock's analysts, free and keyless, asked by the analyst job alone.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
public interface IAnalystFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    // The stock's analysts, none where Yahoo holds none for it.
    Task<YahooAnalystAnswer?> AnalystsAsync(string ticker, CancellationToken cancellation = default);
}

public static class YahooAnalysts
{
    // The summary's parts the job asks for: the targets, the counts by month, the firms' actions, and the price part for
    // the currency the targets are in.
    public const string Modules = "financialData,recommendationTrend,upgradeDowngradeHistory,price";

    // An answer as Yahoo sends it: a summary whose one result holds each part asked for. A summary holding no result, as
    // Yahoo answers a symbol it does not know, and one whose parts hold no target, no month and no action read as none;
    // an answer that is no object, or a result that is no object, is refused.
    public static YahooAnalystAnswer? Parse(string json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"Yahoo's analysts are not JSON: {failure.Message}", failure);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("quoteSummary", out var summary) || summary.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Yahoo's analysts answer holds no quote summary object.");
            }

            if (!summary.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            var result = results[0];

            if (result.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Yahoo's analysts result is a {result.ValueKind} where an object was expected.");
            }

            var financial = Part(result, "financialData");
            var months = new List<YahooMonth>();
            var actions = new List<YahooAction>();

            if (Part(result, "recommendationTrend") is { } trend && trend.TryGetProperty("trend", out var trendRows) && trendRows.ValueKind == JsonValueKind.Array)
            {
                foreach (var month in trendRows.EnumerateArray())
                {
                    if (month.ValueKind != JsonValueKind.Object || Text(month, "period") is not { } period)
                    {
                        continue;
                    }

                    months.Add(new YahooMonth(period, Count(month, "strongBuy"), Count(month, "buy"), Count(month, "hold"), Count(month, "sell"), Count(month, "strongSell")));
                }
            }

            if (Part(result, "upgradeDowngradeHistory") is { } history && history.TryGetProperty("history", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object
                        || !row.TryGetProperty("epochGradeDate", out var graded)
                        || graded.ValueKind != JsonValueKind.Number
                        || !graded.TryGetInt64(out var seconds)
                        || Text(row, "firm") is not { } firm)
                    {
                        continue;
                    }

                    actions.Add(new YahooAction(
                        DateTimeOffset.FromUnixTimeSeconds(seconds),
                        firm,
                        Text(row, "action") ?? string.Empty,
                        Text(row, "fromGrade"),
                        Text(row, "toGrade"),
                        Target(row, "priorPriceTarget"),
                        Target(row, "currentPriceTarget"),
                        Text(row, "priceTargetAction")));
                }
            }

            var answer = new YahooAnalystAnswer(
                Part(result, "price") is { } price ? Text(price, "currency") : null,
                financial is { } low ? Target(low, "targetLowPrice") : null,
                financial is { } mean ? Target(mean, "targetMeanPrice") : null,
                financial is { } median ? Target(median, "targetMedianPrice") : null,
                financial is { } high ? Target(high, "targetHighPrice") : null,
                financial is { } opinions && Raw(opinions, "numberOfAnalystOpinions") is { } counted && counted.TryGetInt32(out var analysts) && analysts > 0 ? analysts : null,
                financial is { } key ? Text(key, "recommendationKey") is { } word && word != "none" ? word : null : null,
                months,
                [.. actions.OrderByDescending(action => action.GradedAt)]);

            return answer.TargetMean is null && answer.TargetLow is null && answer.TargetHigh is null && answer.Months.Count == 0 && answer.Actions.Count == 0
                ? null
                : answer;
        }
    }

    // A part of the result, none where Yahoo sent none or sent it as no object.
    static JsonElement? Part(JsonElement result, string name) =>
        result.TryGetProperty(name, out var part) && part.ValueKind == JsonValueKind.Object ? part : null;

    // A figure as Yahoo writes it, a bare number or an object carrying the number under "raw".
    static JsonElement? Raw(JsonElement holder, string name)
    {
        if (!holder.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("raw", out var raw))
        {
            value = raw;
        }

        return value.ValueKind == JsonValueKind.Number ? value : null;
    }

    // A price read from Yahoo's own digits so none is lost to a double on the way, and none where Yahoo writes none or
    // nought, which it writes for a target a firm did not state.
    static decimal? Target(JsonElement holder, string name) =>
        Raw(holder, name) is { } value
            && decimal.TryParse(value.GetRawText(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var price)
            && price > 0m
            ? price
            : null;

    static int Count(JsonElement holder, string name) =>
        Raw(holder, name) is { } value && value.TryGetInt32(out var count) && count > 0 ? count : 0;

    // A word Yahoo sent, none where it sent none or an empty one.
    static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
