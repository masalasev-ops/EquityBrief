using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Loop;

// What an answer was read into: the case for and the case against, or the cause it was refused for.
public sealed record CaseWordsRead(string? For, string? Against, string? Cause);

// The local model's ask for a case's words, closed: the instruction it is given, the prompt a stored case's figures make
// with no ticker, the schema its answer is held to, and the reader of its answer. The model writes two short arguments
// alone; code writes every figure beside them, so an answer holding a digit, a ticker or a company's name is refused.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public static class CasePrompt
{
    // An answer's budget, enough for two short arguments in one object.
    public const int AnswerTokens = 400;

    public const string NoWords = "no case for and case against";
    public const string HoldsDigit = "a number in its words";
    public const string HoldsTicker = "a ticker in its words";
    public const string HoldsName = "a company's name in its words";

    public static IReadOnlyList<string> Causes { get; } =
    [
        ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NoWords, HoldsDigit, HoldsTicker, HoldsName,
        ReleaseAnswers.RuntimeRefused, ReleaseAnswers.NotAnsweredInTime,
    ];

    // The causes read off the answer itself, which are asked once more before the answer is stored refused.
    public static IReadOnlyList<string> AnswerCauses { get; } =
        [ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NoWords, HoldsDigit, HoldsTicker, HoldsName];

    public const string Instruction =
        "You write the two short arguments an operator reads before deciding whether a variant of a stock-picking rule should "
        + "replace the rule it runs beside. You are given the variant's change and code's figures for both over the same nights "
        + "and over the later years of a long history. Write the case for promoting the variant in one or two plain sentences, "
        + "and the case against it in one or two plain sentences, the case against saying how its lead could still be luck. "
        + "Write no number, no digit, no percentage, no ticker and no company's name: the operator reads the figures beside "
        + "your words. Reply with one JSON object and nothing else, shaped as {\"for\": \"the case for\", \"against\": "
        + "\"the case against\"}.";

    // The instruction's version, the first twelve hex characters of the SHA-256 of its bytes.
    public static string Version { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction)))[..12];

    // The schema the answer is held to: two strings holding no digit, and nothing else.
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["for"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["pattern"] = "^[^0-9]*$" },
            ["against"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["pattern"] = "^[^0-9]*$" },
        },
        ["required"] = new JsonArray("for", "against"),
        ["additionalProperties"] = false,
    };

    // The prompt a stored case makes: the setup and its index, the variant's change, both records over the same nights and
    // over the long history's later years, and how many picks each listed a night, with no ticker and no date.
    public static string For(PromotionCaseView one, string setup)
    {
        var text = new StringBuilder();
        var unit = one.Book ? "points a month" : "per $1 risked";

        text.Append(CultureInfo.InvariantCulture, $"Setup: {setup}, on the {one.IndexName}\n");
        text.Append(CultureInfo.InvariantCulture, $"The variant's change: {string.Join(" ", one.Changes)}\n");
        text.Append(CultureInfo.InvariantCulture, $"Over the same nights, {unit}: the variant {Figure(one.Same.Figure)} over {Count(one)}; the running revision {Figure(one.RunningSame.Figure)} over {(one.Book ? one.RunningSame.Months + " months" : one.RunningSame.Closed + " closed trades")}\n");
        text.Append(CultureInfo.InvariantCulture, $"Over the long history's later years, {unit}: the variant {Figure(one.Long?.Figure)}; the running revision {Figure(one.RunningLong?.Figure)}\n");
        text.Append(CultureInfo.InvariantCulture, $"Picks a night over the same {one.Sessions} nights: the variant {one.Same.Listed}, the running revision {one.RunningSame.Listed}, {one.Shared} of the variant's picks the revision's too\n");

        return text.ToString();
    }

    static string Count(PromotionCaseView one) => one.Book ? one.Same.Months + " months" : one.Same.Closed + " closed trades";

    static string Figure(double? figure) => figure is { } made ? made.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "not read";

    static readonly Regex Digit = new("[0-9]", RegexOptions.CultureInvariant);

    static readonly Regex Capitals = new(@"\b[A-Z]{2,5}(?:\.[A-Z])?\b", RegexOptions.CultureInvariant);

    // An answer read into its two arguments, or the cause it cannot be: one object holding both as strings, neither empty,
    // neither holding a digit, a ticker of the tickers given written in capitals, or a company's name of those given as
    // whole words.
    public static CaseWordsRead Read(string? content, string? finishReason, IReadOnlySet<string> tickers, IReadOnlyCollection<string> names)
    {
        if (string.Equals(finishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal))
        {
            return new(null, null, ReleaseAnswers.CutOff);
        }

        var text = (content ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new(null, null, ReleaseAnswers.Empty);
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(null, null, ReleaseAnswers.NotOneObject);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("for", out var given) || given.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("against", out var against) || against.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(given.GetString()) || string.IsNullOrWhiteSpace(against.GetString()))
        {
            return new(null, null, root.ValueKind == JsonValueKind.Object ? NoWords : ReleaseAnswers.NotOneObject);
        }

        var forWords = given.GetString()!.Trim();
        var againstWords = against.GetString()!.Trim();

        return new(forWords, againstWords, Refusal(forWords + "\n" + againstWords, tickers, names));
    }

    // Why words are refused, none where they hold no digit, no ticker and no company's name.
    public static string? Refusal(string words, IReadOnlySet<string> tickers, IReadOnlyCollection<string> names)
    {
        if (Digit.IsMatch(words))
        {
            return HoldsDigit;
        }

        if (Capitals.Matches(words).Any(match => tickers.Contains(match.Value)))
        {
            return HoldsTicker;
        }

        return names.Any(name => Named(words, name)) ? HoldsName : null;
    }

    // Whether words name a company: its name shorn of the words every company's name ends in, at least four letters long,
    // standing as whole words with its own capitals.
    static bool Named(string words, string name)
    {
        var core = Shorn(name);

        return core.Length >= 4 && Regex.IsMatch(words, @"(?<![\p{L}\p{N}])" + Regex.Escape(core) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
    }

    static readonly string[] Endings = ["Incorporated", "Inc.", "Inc", "Corporation", "Corp.", "Corp", "Company", "Co.", "Co", "Ltd.", "Ltd", "plc", "PLC", "Holdings", "Group", "N.V.", "S.A.", "L.P.", "LP", "Trust", "Class A", "Class B", "& Co."];

    public static string Shorn(string name)
    {
        var core = name.Trim().TrimEnd(',', '.');
        var changed = true;

        if (core.StartsWith("The ", StringComparison.Ordinal))
        {
            core = core[4..];
        }

        while (changed)
        {
            changed = false;

            foreach (var ending in Endings)
            {
                if (core.EndsWith(" " + ending, StringComparison.Ordinal) || core.EndsWith("," + ending, StringComparison.Ordinal))
                {
                    core = core[..^(ending.Length + 1)].TrimEnd(',', ' ', '.');
                    changed = true;
                }
            }
        }

        return core;
    }
}
