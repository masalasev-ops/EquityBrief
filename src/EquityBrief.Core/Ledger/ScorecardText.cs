using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Ledger;

// The scorecard's checks as the store keeps them: a list in the checks' order, each its number, its answer in words,
// its amounts as decimal text, its ratios in the invariant form and the line not filed, read back as written.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public static class ScorecardText
{
    public static string Word(CheckAnswer answer) => answer switch
    {
        CheckAnswer.Passed => "passed",
        CheckAnswer.Failed => "failed",
        CheckAnswer.NotFiled => "not filed",
        _ => "not applying",
    };

    public static CheckAnswer AnswerOf(string word) => word switch
    {
        "passed" => CheckAnswer.Passed,
        "failed" => CheckAnswer.Failed,
        "not filed" => CheckAnswer.NotFiled,
        "not applying" => CheckAnswer.NotApplying,
        _ => throw new FormatException($"A stored check's answer '{word}' is none the scorecard writes."),
    };

    public static string WordOf(ScorecardWord word) => word switch
    {
        ScorecardWord.Strong => "strong",
        ScorecardWord.Mixed => "mixed",
        _ => "weak",
    };

    public static ScorecardWord ScorecardWordOf(string word) => word switch
    {
        "strong" => ScorecardWord.Strong,
        "mixed" => ScorecardWord.Mixed,
        "weak" => ScorecardWord.Weak,
        _ => throw new FormatException($"A stored scorecard's word '{word}' is none the scorecard writes."),
    };

    public static string Of(IReadOnlyList<ScorecardCheck> checks)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var check in checks)
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", check.Number);
                writer.WriteString("answer", Word(check.Answer));
                Text(writer, "amount", check.Amount);
                Text(writer, "against", check.Against);
                Ratio(writer, "now", check.Now);
                Ratio(writer, "yearBefore", check.YearBefore);

                if (check.Missing is { } missing)
                {
                    writer.WriteString("missing", missing);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static IReadOnlyList<ScorecardCheck> ChecksOf(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.EnumerateArray().Select(check => new ScorecardCheck(
                check.GetProperty("number").GetInt32(),
                AnswerOf(check.GetProperty("answer").GetString()!),
                check.TryGetProperty("amount", out var amount) ? decimal.Parse(amount.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture) : null,
                check.TryGetProperty("against", out var against) ? decimal.Parse(against.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture) : null,
                check.TryGetProperty("now", out var now) ? double.Parse(now.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture) : null,
                check.TryGetProperty("yearBefore", out var before) ? double.Parse(before.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture) : null,
                check.TryGetProperty("missing", out var missing) ? missing.GetString() : null)),
        ];
    }

    static void Text(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is { } figure)
        {
            writer.WriteString(name, figure.ToString(CultureInfo.InvariantCulture));
        }
    }

    static void Ratio(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } figure)
        {
            writer.WriteString(name, figure.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
