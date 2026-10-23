using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;

namespace EquityBrief.Data.Analysts;

// How the analysts' answers are kept as lists in their rows and read back: each month's counts, each firm's action,
// each span's counts and each analyst's rating as a JSON object, its prices as text in the store's own form so no figure
// passes through a double, its instants and days as the store writes them.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public static class StoredAnalysts
{
    const string Instant = "yyyy-MM-ddTHH:mm:ssZ";

    const string Day = "yyyy-MM-dd";

    public static string Months(IReadOnlyList<YahooMonth> months) =>
        Written(writer =>
        {
            foreach (var month in months)
            {
                writer.WriteStartObject();
                writer.WriteString("period", month.Period);
                writer.WriteNumber("strongBuy", month.StrongBuy);
                writer.WriteNumber("buy", month.Buy);
                writer.WriteNumber("hold", month.Hold);
                writer.WriteNumber("sell", month.Sell);
                writer.WriteNumber("strongSell", month.StrongSell);
                writer.WriteEndObject();
            }
        });

    public static IReadOnlyList<YahooMonth> MonthsOf(string json) =>
        Read(json, month => new YahooMonth(
            month.GetProperty("period").GetString()!,
            month.GetProperty("strongBuy").GetInt32(),
            month.GetProperty("buy").GetInt32(),
            month.GetProperty("hold").GetInt32(),
            month.GetProperty("sell").GetInt32(),
            month.GetProperty("strongSell").GetInt32()));

    public static string Actions(IReadOnlyList<YahooAction> actions) =>
        Written(writer =>
        {
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("gradedAt", action.GradedAt.UtcDateTime.ToString(Instant, CultureInfo.InvariantCulture));
                writer.WriteString("firm", action.Firm);
                writer.WriteString("action", action.Action);
                Text(writer, "fromGrade", action.FromGrade);
                Text(writer, "toGrade", action.ToGrade);
                Text(writer, "priorTarget", action.PriorTarget is { } prior ? Money.ToStorage(prior) : null);
                Text(writer, "target", action.Target is { } target ? Money.ToStorage(target) : null);
                Text(writer, "targetAction", action.TargetAction);
                writer.WriteEndObject();
            }
        });

    public static IReadOnlyList<YahooAction> ActionsOf(string json) =>
        Read(json, action => new YahooAction(
            DateTimeOffset.ParseExact(action.GetProperty("gradedAt").GetString()!, Instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            action.GetProperty("firm").GetString()!,
            action.GetProperty("action").GetString()!,
            TextOf(action, "fromGrade"),
            TextOf(action, "toGrade"),
            TextOf(action, "priorTarget") is { } prior ? Money.FromStorage(prior) : null,
            TextOf(action, "target") is { } target ? Money.FromStorage(target) : null,
            TextOf(action, "targetAction")));

    public static string Counts(IReadOnlyList<TipRanksCounts> counts) =>
        Written(writer =>
        {
            foreach (var span in counts)
            {
                writer.WriteStartObject();
                writer.WriteString("span", span.Span);
                writer.WriteNumber("analysts", span.Analysts);
                writer.WriteNumber("buy", span.Buy);
                writer.WriteNumber("hold", span.Hold);
                writer.WriteNumber("sell", span.Sell);
                writer.WriteEndObject();
            }
        });

    public static IReadOnlyList<TipRanksCounts> CountsOf(string json) =>
        Read(json, span => new TipRanksCounts(
            span.GetProperty("span").GetString()!,
            span.GetProperty("analysts").GetInt32(),
            span.GetProperty("buy").GetInt32(),
            span.GetProperty("hold").GetInt32(),
            span.GetProperty("sell").GetInt32()));

    public static string Ratings(IReadOnlyList<TipRanksRating> ratings) =>
        Written(writer =>
        {
            foreach (var rating in ratings)
            {
                writer.WriteStartObject();
                writer.WriteString("analyst", rating.Analyst);
                Text(writer, "firm", rating.Firm);
                Text(writer, "rating", rating.Rating);
                Text(writer, "action", rating.Action);
                Text(writer, "ratedOn", rating.RatedOn?.ToString(Day, CultureInfo.InvariantCulture));
                Text(writer, "target", rating.Target is { } target ? Money.ToStorage(target) : null);
                Text(writer, "currency", rating.Currency);
                Number(writer, "stars", rating.Stars);
                Number(writer, "successRate", rating.SuccessRate);
                Number(writer, "averageReturn", rating.AverageReturn);
                Whole(writer, "rank", rating.Rank);
                Whole(writer, "ratings", rating.Ratings);
                Text(writer, "article", rating.Article);
                writer.WriteEndObject();
            }
        });

    public static IReadOnlyList<TipRanksRating> RatingsOf(string json) =>
        Read(json, rating => new TipRanksRating(
            rating.GetProperty("analyst").GetString()!,
            TextOf(rating, "firm"),
            TextOf(rating, "rating"),
            TextOf(rating, "action"),
            TextOf(rating, "ratedOn") is { } day ? DateOnly.ParseExact(day, Day, CultureInfo.InvariantCulture) : null,
            TextOf(rating, "target") is { } target ? Money.FromStorage(target) : null,
            TextOf(rating, "currency"),
            NumberOf(rating, "stars"),
            NumberOf(rating, "successRate"),
            NumberOf(rating, "averageReturn"),
            WholeOf(rating, "rank"),
            WholeOf(rating, "ratings"),
            TextOf(rating, "article")));

    static string Written(Action<Utf8JsonWriter> rows)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            rows(writer);
            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    static IReadOnlyList<T> Read<T>(string json, Func<JsonElement, T> row)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(row)];
    }

    static void Text(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    static void Number(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    static void Whole(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is { } whole)
        {
            writer.WriteNumber(name, whole);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    static string? TextOf(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static double? NumberOf(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    static int? WholeOf(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
