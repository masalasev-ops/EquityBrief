using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Judge;

// The naming test: the local model asked to name the company behind a seeded sample of a night's short rows and full
// sheets, each asked alone. It passes where the model names at most 2 in 100 of either round, an answer naming the
// company where its ticker is the member's or its words hold the member's name as the names refresh keeps it; and the
// judge starts under the field list's version only where the newest test under that version passed.
// see: The AI stock picker starts only where the local model names no more than 2 in 100 of the companies behind a sample of its facts
public static partial class JudgeNaming
{
    public const int SampleSize = 100;

    public const int MostNamed = 2;

    // What the naming and refusal bounds are counted in.
    public const int Hundred = 100;

    public const int AnswerTokens = 60;

    public const string Row = "row";

    public const string Sheet = "sheet";

    public const string System =
        "You will read facts about one company listed in the United States. Its name, its ticker, its industry and every date have been removed. "
        + "If you can tell which company it is, give its name and its ticker; if you cannot, answer unknown for both.";

    public static string Prompt(string facts) => "The facts:\n" + facts;

    // The answer the model is held to: the company's name and its ticker, each a word or unknown. The object schema
    // alone, since the feed wraps it in the runtime's structured output.
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["company"] = new JsonObject { ["type"] = "string" },
            ["ticker"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("company", "ticker"),
        ["additionalProperties"] = false,
    };

    // Whether an answer names the member: its ticker is the member's, or the answer's company holds the first word of the
    // member's name that is no word every company name shares, read without case or punctuation.
    public static bool Named(string? company, string? ticker, string memberTicker, string? memberName)
    {
        if (!string.IsNullOrWhiteSpace(ticker) && string.Equals(Plain(ticker).Replace(" ", string.Empty, StringComparison.Ordinal), Plain(memberTicker).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        var answer = Words(company);
        var key = Words(memberName).FirstOrDefault(word => word.Length >= 3);

        return key is not null && answer.Contains(key);
    }

    // Whether a round's answers pass: at most 2 in 100 of those asked named the company, none passing where none was asked.
    public static bool Passed(int named, int asked) => asked > 0 && named * Hundred <= MostNamed * asked;

    // A name's words without case or punctuation, the words every company name shares left out.
    static IReadOnlyList<string> Words(string text) =>
        [.. Plain(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !Shared.Contains(word))];

    static string Plain(string text) => Punctuation().Replace(text.ToLower(CultureInfo.InvariantCulture), " ").Trim();

    static readonly HashSet<string> Shared = new(StringComparer.Ordinal)
    {
        "the", "inc", "incorporated", "corp", "corporation", "co", "company", "companies", "ltd", "limited", "plc", "llc",
        "lp", "holdings", "holding", "group", "class", "common", "stock", "shares", "ordinary", "of", "and", "unknown",
    };

    [GeneratedRegex("[^a-z0-9 ]+")]
    private static partial Regex Punctuation();
}
