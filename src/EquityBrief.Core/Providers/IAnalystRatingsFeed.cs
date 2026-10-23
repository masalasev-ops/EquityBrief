using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One analyst's newest rating of a stock as TipRanks files it: the analyst and the firm, the rating and TipRanks' word
// for the action, its day, the target and its currency, and the analyst's record as TipRanks scores it, the stars out of
// five, the share of ratings that made money, the average return over the market in per cent, the rank among every
// analyst it scores and how many ratings the record counts, with the title of the article the rating was reported in.
public sealed record TipRanksRating(
    string Analyst,
    string? Firm,
    string? Rating,
    string? Action,
    DateOnly? RatedOn,
    decimal? Target,
    string? Currency,
    double? Stars,
    double? SuccessRate,
    double? AverageReturn,
    int? Rank,
    int? Ratings,
    string? Article);

// The buys, holds and sells of the analysts rating a stock over one span TipRanks names, "last30Days", "last90Days" or
// "last12Months".
public sealed record TipRanksCounts(string Span, int Analysts, int Buy, int Hold, int Sell);

// TipRanks' answer about a stock's recent ratings: the day it is as of, how many it counts, the counts over each span,
// and each analyst's newest rating, newest first, as TipRanks sent them.
public sealed record TipRanksAnswer(DateOnly? AsOf, int? Total, IReadOnlyList<TipRanksCounts> Counts, IReadOnlyList<TipRanksRating> Ratings);

// The month's calls as TipRanks counts them: how many are used, how many the account allows, and when the count starts
// again.
public sealed record TipRanksUsage(int Used, int Limit, DateTimeOffset ResetsAt);

// TipRanks' server on the operator's key: its count of the month's calls, which costs none, and a stock's recent ratings,
// which cost one, each asked by the ratings job alone.
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
public interface IAnalystRatingsFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    // The month's calls used and allowed, as TipRanks counts them.
    Task<TipRanksUsage> UsageAsync(CancellationToken cancellation = default);

    // The stock's recent ratings, one of the month's calls; none where TipRanks holds none for it.
    Task<TipRanksAnswer?> RatingsAsync(string ticker, CancellationToken cancellation = default);
}

public static class TipRanksAnswers
{
    // The text of a tool's answer: the server's answer, plain or as the last event of a stream, holds a result whose
    // first content is the tool's own JSON as text. An error the server or the tool states is refused with its words;
    // an answer that is neither is refused as unreadable.
    public static string ToolText(string body)
    {
        var json = body.TrimStart().StartsWith('{')
            ? body
            : body.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()).LastOrDefault()
              ?? throw new FormatException("TipRanks' answer holds no JSON and no event carrying any.");

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"TipRanks' answer is not JSON: {failure.Message}", failure);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"TipRanks' answer is a {root.ValueKind} where an object was expected.");
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                throw new ProviderRefusal($"TipRanks refused the call: {Text(error, "message") ?? "no reason given"}.", transient: false);
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
            {
                throw new FormatException("TipRanks' answer holds no result content.");
            }

            var text = Text(content[0], "text") ?? throw new FormatException("TipRanks' answer's first content holds no text.");

            if (result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
            {
                throw new ProviderRefusal($"TipRanks answered the call with an error: {text}", transient: false);
            }

            return text;
        }
    }

    // The month's calls as the count tool states them: used, the limit and the instant the count starts again.
    public static TipRanksUsage Usage(string body)
    {
        using var document = Tool(ToolText(body));
        var root = document.RootElement;

        return Whole(root, "used") is { } used
            && Whole(root, "limit") is { } limit
            && Text(root, "resets_at") is { } resets
            && DateTimeOffset.TryParse(resets, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? new TipRanksUsage(used, limit, at)
            : throw new FormatException("TipRanks' count of the month's calls holds no used count, limit or reset.");
    }

    // A stock's recent ratings as the ratings tool states them, none where it counts none and lists none.
    public static TipRanksAnswer? Ratings(string body)
    {
        using var document = Tool(ToolText(body));
        var root = document.RootElement;
        var counts = new List<TipRanksCounts>();
        var ratings = new List<TipRanksRating>();

        if (root.TryGetProperty("counts", out var spans) && spans.ValueKind == JsonValueKind.Object)
        {
            foreach (var span in spans.EnumerateObject().Where(span => span.Value.ValueKind == JsonValueKind.Object))
            {
                counts.Add(new TipRanksCounts(span.Name, Whole(span.Value, "analysts") ?? 0, Whole(span.Value, "buy") ?? 0, Whole(span.Value, "hold") ?? 0, Whole(span.Value, "sell") ?? 0));
            }
        }

        if (root.TryGetProperty("ratings", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || Text(row, "analystName") is not { } analyst)
                {
                    continue;
                }

                ratings.Add(new TipRanksRating(
                    analyst,
                    Text(row, "firmName"),
                    Text(row, "recommendation"),
                    Text(row, "analystAction"),
                    Text(row, "recommendationDate") is { } day && DateOnly.TryParseExact(day, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rated) ? rated : null,
                    row.TryGetProperty("priceTarget", out var target) && target.ValueKind == JsonValueKind.Number
                        && decimal.TryParse(target.GetRawText(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var price) && price > 0m
                        ? price
                        : null,
                    Text(row, "priceTargetCurrencyCode"),
                    Fraction(row, "numOfStars"),
                    Fraction(row, "successRate"),
                    Fraction(row, "excessReturn"),
                    Whole(row, "analystRank"),
                    Whole(row, "totalRecommendations"),
                    Text(row, "articleTitle")));
            }
        }

        if (ratings.Count == 0 && counts.All(span => span.Analysts == 0))
        {
            return null;
        }

        return new TipRanksAnswer(
            Text(root, "asOf") is { } asOf && DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stated) ? stated : null,
            Whole(root, "totalRatings"),
            counts,
            [.. ratings.OrderByDescending(rating => rating.RatedOn ?? DateOnly.MinValue)]);
    }

    // The tool's own JSON, refused where it is no object.
    static JsonDocument Tool(string text)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"TipRanks' tool answered text that is not JSON: {failure.Message}", failure);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();

            throw new FormatException("TipRanks' tool answered JSON that is no object.");
        }

        return document;
    }

    static int? Whole(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var whole) ? whole : null;

    static double? Fraction(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
