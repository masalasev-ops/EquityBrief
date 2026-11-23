using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Loop;

// An idea the notebook holds for a setup on an index, as the researcher is shown it: its grammar line, its edge per $1
// risked over the early years, and the later years as its verdict's one word.
public sealed record ResearchNoted(string Line, double? EarlyEdge, string Verdict);

// What one setup on one index puts to the researcher: its failure list over the early years and over the nights since the
// rules restarted, with the closed picks and losers each read, and the ideas the notebook holds for it.
public sealed record ResearchInput(
    string Family,
    string Index,
    IReadOnlyList<FailureRow> Early,
    int EarlyPicks,
    int EarlyLosers,
    IReadOnlyList<FailureRow> Live,
    int LivePicks,
    int LiveLosers,
    IReadOnlyList<ResearchNoted> Noted);

// One idea the researcher answered: its line as code wrote it from the answer's closed choices, and its one sentence.
public sealed record ResearchIdea(IdeaLine Line, string Reason);

// What an answer was read into: its idea, none where it answered that no reading separates the losers, or the cause it was
// refused for.
public sealed record ResearchAnswerRead(ResearchIdea? Idea, bool None, string? Cause);

// The researcher's ask, closed: the instruction, the prompt one setup on one index makes with no name, no ticker, no date
// and no figure of the later years, the schema its answer is held to, and the reader of its answer. Each ask answers one
// idea as one flat object, since the runtime holds a flat object's keys and choices to its schema and an array's later
// items to none; a setup is asked for up to three, each later ask shown the ideas it has given. The model answers closed
// choices from the grammar's own lists, a band of tenths as one of the bands the grammar reads, and one sentence holding no
// digit; code writes the line from them, the setup's own event on its index, its own time limit and its five a night, so
// every idea it reads is one its setup can run as a variant.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
// see: An idea is one line of a closed grammar that code parses into one canonical line and tests with no build session
public static class ResearchPrompt
{
    // The most ideas a setup is asked for a week.
    public const int MostIdeas = 3;

    // An answer's budget, enough for one idea in one object.
    public const int AnswerTokens = 600;

    public const string NotInGrammar = "an idea the grammar cannot read";
    public const string NoReason = "an idea with no sentence of reasoning";
    public const string HoldsDigit = "a number in its sentence";

    public static IReadOnlyList<string> Causes { get; } =
    [
        ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NotInGrammar, NoReason, HoldsDigit,
        ReleaseAnswers.RuntimeRefused, ReleaseAnswers.NotAnsweredInTime,
    ];

    // The causes read off the answer itself, which are asked once more before the answer is stored refused.
    public static IReadOnlyList<string> AnswerCauses { get; } =
        [ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NotInGrammar, NoReason, HoldsDigit];

    public const string Instruction =
        "You are a researcher improving one stock-picking rule. You are shown where its losing picks gather: for a reading of "
        + "a stock on the day the rule picked it, the share of the rule's losers standing in the reading's lowest or highest "
        + "three tenths against the share of all its picks there, first over the early years of a long history and then over "
        + "the nights since the rule restarted, a loser a pick that did no better than the same plan bought on every stock "
        + "that day. You are also shown the ideas already tested for this rule, each with how it did on the early years and "
        + "one word for the later years, which you never see, and any idea you have already given this week. Propose the "
        + "strongest new idea, one that keeps the rule's picks out of where its losers gather or into where they are fewest. "
        + "An idea keeps the picks whose readings stand in a band of their tenths, the tenths numbered one, the lowest, to ten: "
        + "its first condition on one reading, and a second and a third on other readings or none; it chooses how it buys, its "
        + "stop in typical days under the buy and how it takes profit, and orders a night's picks by one reading, highest or "
        + "lowest first. Never propose an idea already tested or already given. Give one plain sentence of reasoning holding "
        + "no number, no digit, no ticker and no company's name. Reply with one JSON object and nothing else, as the schema "
        + "shapes it, with has_idea false where no reading separates the losers.";

    // The instruction's version, the first twelve hex characters of the SHA-256 of its bytes.
    public static string Version { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction)))[..12];

    // The buy's choices as the answer names them: the next open, or a zone or the average within a window.
    public const string NextOpen = "next open";

    // A condition the answer leaves out.
    public const string NoCondition = "none";

    static IReadOnlyList<string> Buys => [NextOpen, .. IdeaGrammar.Entries];

    // Every band of tenths a condition reads, as the answer names it: two tenths or more and never all ten.
    public static IReadOnlyList<string> Bands { get; } =
    [
        .. from lowest in Enumerable.Range(1, IdeaGrammar.Tenths)
           from highest in Enumerable.Range(lowest + 1, IdeaGrammar.Tenths - lowest)
           where !(lowest == 1 && highest == IdeaGrammar.Tenths)
           select BandOf(lowest, highest),
    ];

    public static string BandOf(int lowest, int highest) => FormattableString.Invariant($"tenths {lowest} to {highest}");

    static readonly Regex BandWords = new("^tenths ([0-9]+) to ([0-9]+)$", RegexOptions.CultureInvariant);

    // The schema the answer is held to: one flat object of closed choices from the grammar's own lists and one sentence
    // holding no digit, and nothing else.
    public static JsonObject Schema()
    {
        static JsonObject Of(IEnumerable<string> values) => new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]) };

        var properties = new JsonObject { ["has_idea"] = new JsonObject { ["type"] = "boolean" } };

        for (var at = 1; at <= IdeaGrammar.MostConditions; at++)
        {
            properties[$"condition_{at}_reading"] = Of(at == 1 ? IdeaGrammar.Readings : [NoCondition, .. IdeaGrammar.Readings]);
            properties[$"condition_{at}_band"] = Of(Bands);
        }

        properties["buy"] = Of(Buys);
        properties["window"] = Of(IdeaGrammar.Windows.Select(window => window.ToString(CultureInfo.InvariantCulture)));
        properties["stop"] = Of(IdeaGrammar.StopDays.Select(IdeaGrammar.Number));
        properties["take_profit"] = Of([IdeaGrammar.TimeOnly, IdeaGrammar.Whole, IdeaGrammar.Half]);
        properties["multiple"] = Of(IdeaGrammar.Multiples.Select(IdeaGrammar.Number));
        properties["order"] = Of(IdeaGrammar.Readings);
        properties["order_highest"] = new JsonObject { ["type"] = "boolean" };
        properties["reason"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[^0-9]*$" };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. properties.Select(property => (JsonNode)property.Key)]),
            ["additionalProperties"] = false,
        };
    }

    // The prompt one setup on one index makes: the setup in words with its time limit and its five a night; each failure
    // list with the closed picks and losers it read, the early years' and the live nights' apart; the ideas the notebook
    // holds for it, each with its early edge and its later verdict's one word; and the lines already given this week. No
    // ticker, no date and no figure of the later years stands in it.
    public static string For(ResearchInput input, IReadOnlyList<string>? given = null)
    {
        var text = new StringBuilder();
        var family = IdeaGrammar.FamilyWords(input.Family);

        text.Append(CultureInfo.InvariantCulture, $"Rule: the {char.ToLowerInvariant(family[0])}{family[1..]} on the {IdeaGrammar.IndexWords(input.Index)}. It buys at most {SetupFamilies.ListedANight} a night and sells after {IdeaGrammar.FamilyTimeLimit(input.Family)} sessions.\n\n");

        List(text, "the early years", input.Early, input.EarlyPicks, input.EarlyLosers);
        List(text, "the nights since the rule restarted", input.Live, input.LivePicks, input.LiveLosers);

        text.Append("Ideas already tested for this rule, which you must not propose again:\n");

        if (input.Noted.Count == 0)
        {
            text.Append("none yet\n");
        }

        foreach (var noted in input.Noted)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {noted.Line} | the early years, per $1 risked: {Figure(noted.EarlyEdge)} | the later years: {noted.Verdict}\n");
        }

        if (given is { Count: > 0 })
        {
            text.Append("\nIdeas you have already given this week, which you must not give again:\n");

            foreach (var line in given)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {line}\n");
            }
        }

        return text.ToString();
    }

    static void List(StringBuilder text, string span, IReadOnlyList<FailureRow> rows, int picks, int losers)
    {
        text.Append(CultureInfo.InvariantCulture, $"Where its losers gather over {span}, of its {picks} closed picks, {losers} losers:\n");

        if (rows.Count == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"no reading holds {FailureList.LeastInBand} of its closed picks in a band where its losers gather\n\n");

            return;
        }

        text.Append("reading | band | picks in the band | losers in the band | share of its losers | share of its picks\n");

        foreach (var row in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"{row.Reading} | {row.Band} | {row.InBand} | {row.LosersInBand} | {Share(row.LosersShare)} | {Share(row.PicksShare)}\n");
        }

        text.Append('\n');
    }

    static string Share(double share) => share.ToString("0%", CultureInfo.InvariantCulture);

    static string Figure(double? figure) => figure is { } made ? made.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "not read";

    // The key an answer is stored under: the instruction's version and the prompt, hashed.
    public static string Key(string prompt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + prompt)))[..32];

    static readonly Regex Digit = new("[0-9]", RegexOptions.CultureInvariant);

    // The line an idea's closed choices write: the setup's own event on its index, each condition's band, the buy, the stop,
    // the take-profit, the setup's own time limit and its five a night in the order chosen.
    public static string LineOf(string family, string index, IReadOnlyList<(string Reading, int Lowest, int Highest)> conditions, string buy, int window, double stop, string profit, double multiple, string order, bool highest)
    {
        var parts = new List<string> { FormattableString.Invariant($"event {family}({IdeaGrammar.WordOf(index)})") };

        parts.AddRange(conditions.Select(condition => new IdeaCondition(condition.Reading, condition.Lowest, condition.Highest).Line));
        parts.Add(buy is NextOpen or PlanPoints.NextOpen ? "buy next open" : FormattableString.Invariant($"buy at {buy} within {window} sessions"));
        parts.Add("stop " + IdeaGrammar.Number(stop) + " typical days");
        parts.Add(profit == IdeaGrammar.TimeOnly ? "take profit none" : FormattableString.Invariant($"take profit {profit} at {IdeaGrammar.Number(multiple)} risks"));
        parts.Add(FormattableString.Invariant($"sell after {IdeaGrammar.FamilyTimeLimit(family)} sessions"));
        parts.Add(FormattableString.Invariant($"cap {SetupFamilies.ListedANight} a night by {order} {(highest ? "highest" : "lowest")}"));

        return string.Join(IdeaGrammar.Separator, parts);
    }

    // An answer read into its idea, or the cause it cannot be: one object; has_idea false an answer of no idea; otherwise
    // its closed choices writing a line the grammar parses, a second or third condition on a reading already named passed
    // over, with a sentence holding no digit. A line break inside the sentence reads as a space.
    public static ResearchAnswerRead Read(string? content, string? finishReason, string family, string index)
    {
        if (string.Equals(finishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal))
        {
            return new(null, false, ReleaseAnswers.CutOff);
        }

        var text = (content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (text.Length == 0)
        {
            return new(null, false, ReleaseAnswers.Empty);
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(null, false, ReleaseAnswers.NotOneObject);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(null, false, ReleaseAnswers.NotOneObject);
        }

        if (root.TryGetProperty("has_idea", out var has) && has.ValueKind == JsonValueKind.False)
        {
            return new(null, true, null);
        }

        if (!root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reason.GetString()))
        {
            return new(null, false, NoReason);
        }

        var sentence = string.Join(' ', reason.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (Digit.IsMatch(sentence))
        {
            return new(null, false, HoldsDigit);
        }

        return LineFrom(root, family, index) is { } line ? new(new ResearchIdea(line, sentence), false, null) : new(null, false, NotInGrammar);
    }

    static IdeaLine? LineFrom(JsonElement idea, string family, string index)
    {
        string? Text(string name) => idea.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        static int? Whole(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

        static double? Real(string? text) => double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        var conditions = new List<(string, int, int)>();

        for (var at = 1; at <= IdeaGrammar.MostConditions; at++)
        {
            var reading = Text($"condition_{at}_reading");

            if (at > 1 && reading == NoCondition)
            {
                continue;
            }

            if (reading is null || !IdeaGrammar.Readings.Contains(reading) || BandWords.Match(Text($"condition_{at}_band") ?? string.Empty) is not { Success: true } band
                || !Bands.Contains(band.Value))
            {
                return null;
            }

            if (conditions.All(one => one.Item1 != reading))
            {
                conditions.Add((reading, int.Parse(band.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(band.Groups[2].Value, CultureInfo.InvariantCulture)));
            }
        }

        if (Text("buy") is not { } buy || !Buys.Contains(buy)
            || Whole(Text("window")) is not { } window
            || Real(Text("stop")) is not { } stop
            || Text("take_profit") is not { } profit
            || Real(Text("multiple")) is not { } multiple
            || Text("order") is not { } order || !IdeaGrammar.Readings.Contains(order)
            || !idea.TryGetProperty("order_highest", out var highestFirst) || highestFirst.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return IdeaGrammar.Parse(LineOf(family, index, conditions, buy, window, stop, profit, multiple, order, highestFirst.GetBoolean())).Line;
    }
}
