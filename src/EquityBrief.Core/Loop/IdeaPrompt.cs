using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Loop;

// One idea the local model answered: a reading of the ledger's catalogue by its column, which side of it a setup is kept
// on, and the tenth of the fold's listings, in the reading's order, at whose upper edge the condition sits.
public sealed record IdeaLabel(string Reading, bool Above, int Tenth);

// What a stored answer was read into: its labels, or the cause it was refused for.
public sealed record IdeaAnswerRead(IReadOnlyList<IdeaLabel>? Labels, string? Cause);

// The idea generator's ask, closed: the instruction it is given, the prompt a fold's own tables make with no ticker and
// no date, the key its answer is stored under, and the reader of its answer. The model answers labels alone, a reading
// from the catalogue's list, a side and a tenth, and is asked for no number; code turns each label into a condition at the
// upper edge of that tenth over the fold's own listings, where winners against losers cuts a reading, so a proposal's
// level is one code found and never one the model wrote.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
public static class IdeaPrompt
{
    // The most ideas an answer may carry, and so the most proposals a family on an index takes from the generator a run.
    public const int MostIdeas = 3;

    // An answer's budget, enough for three ideas in one object.
    public const int AnswerTokens = 512;

    // The causes read off an answer beside the readings' own three: no list of ideas, more than three, a reading the
    // catalogue does not list, a side neither word and a tenth no whole number from one to nine.
    public const string NoIdeas = "no list of ideas";
    public const string TooMany = "more than three ideas";
    public const string ReadingNotListed = "a reading the catalogue does not list";
    public const string SideNotListed = "a side neither above nor below";
    public const string TenthOutside = "a tenth outside one to nine";

    public static IReadOnlyList<string> Causes { get; } =
    [
        ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NoIdeas, TooMany, ReadingNotListed, SideNotListed, TenthOutside,
        ReleaseAnswers.RuntimeRefused, ReleaseAnswers.NotAnsweredInTime,
    ];

    // The causes read off the answer itself, which are asked once more before the answer is stored refused.
    public static IReadOnlyList<string> AnswerCauses { get; } =
        [ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, NoIdeas, TooMany, ReadingNotListed, SideNotListed, TenthOutside];

    public const string Instruction =
        "You read the record of one stock-picking rule: how its finished trades went, and for each reading of a stock on the "
        + "day the rule listed it, over the listings whose trades had finished, how many held the reading, how many of those "
        + "were winners and how many losers, the reading's median among the winners and among the losers, and the mean edge "
        + "of each tenth of those listings in the reading's order, the first tenth the lowest values. Propose at most three "
        + "conditions a listing should also meet so the rule keeps its better listings, the strongest first. Reply with one "
        + "JSON object and nothing else, shaped as {\"ideas\": [{\"reading\": \"a reading as the table writes it\", "
        + "\"side\": \"above\" or \"below\", \"tenth\": a whole number from 1 to 9}]}. A condition reads the reading at the "
        + "upper edge of the tenth you name: above keeps the listings at or above it, the tenths after it; below keeps those "
        + "at or under it, that tenth and the ones before it. Reply {\"ideas\": []} where no reading separates the winners "
        + "from the losers.";

    // The instruction's version, the first twelve hex characters of the SHA-256 of its bytes.
    public static string Version { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction)))[..12];

    // The prompt a fold's tables make: the rule's family, the autopsy's figures over the trades that finished before the
    // fold's year, and each reading's spread over the listings that did, in the catalogue's order, a reading no listing
    // holds left out, with no ticker and no date.
    public static string For(string family, IReadOnlyList<AutopsyFigure> autopsy, IReadOnlyList<ReadingSpread> spreads)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"Rule: the {family}\n\nHow its finished trades went:\n");

        foreach (var figure in autopsy)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {figure.Words}, over {figure.Trades} trades\n");
        }

        text.Append("\nEach reading over the listings whose trades had finished, a winner one whose edge was above nothing:\n");
        text.Append("reading | listings | winners | losers | winners' median | losers' median | mean edge by tenth, lowest first\n");

        foreach (var spread in spreads.Where(spread => spread.Units > 0))
        {
            text.Append(CultureInfo.InvariantCulture, $"{spread.Column} | {spread.Units} | {spread.Winners} | {spread.Losers} | {Figure(spread.WinnersMedian)} | {Figure(spread.LosersMedian)} | {string.Join(" ", spread.Deciles.Select(Figure))}\n");
        }

        return text.ToString();
    }

    // The key an answer is stored under: the instruction's version and the prompt, hashed, so tables a fold made
    // differently are a new ask and a run again over the same tables reads the answer stored.
    public static string Key(string prompt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + prompt)))[..32];

    // An answer read into its labels, or the cause it cannot be: one object, as the readings' answers are, holding a list
    // of at most three ideas, each a reading the catalogue lists written exactly, a side of the two words and a tenth a
    // whole number from one to nine; a key the answer adds is passed over, and an empty list is an answer of no idea.
    public static IdeaAnswerRead Read(string? content, string? finishReason)
    {
        if (string.Equals(finishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal))
        {
            return new(null, ReleaseAnswers.CutOff);
        }

        var text = (content ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new(null, ReleaseAnswers.Empty);
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(null, ReleaseAnswers.NotOneObject);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(null, ReleaseAnswers.NotOneObject);
        }

        if (!root.TryGetProperty("ideas", out var ideas) || ideas.ValueKind != JsonValueKind.Array)
        {
            return new(null, NoIdeas);
        }

        if (ideas.GetArrayLength() > MostIdeas)
        {
            return new(null, TooMany);
        }

        var labels = new List<IdeaLabel>();

        foreach (var idea in ideas.EnumerateArray())
        {
            if (idea.ValueKind != JsonValueKind.Object
                || !idea.TryGetProperty("reading", out var reading)
                || reading.ValueKind != JsonValueKind.String
                || ColumnAt(reading.GetString()!) < 0)
            {
                return new(null, ReadingNotListed);
            }

            if (!idea.TryGetProperty("side", out var side) || side.ValueKind != JsonValueKind.String || side.GetString() is not ("above" or "below"))
            {
                return new(null, SideNotListed);
            }

            if (!idea.TryGetProperty("tenth", out var tenth) || tenth.ValueKind != JsonValueKind.Number || !tenth.TryGetInt32(out var whole) || whole is < 1 or > 9)
            {
                return new(null, TenthOutside);
            }

            labels.Add(new IdeaLabel(reading.GetString()!, side.GetString() == "above", whole));
        }

        return new(labels, null);
    }

    // A label turned into a condition over the fold's own listings: the reading's value at the upper edge of the label's
    // tenth, by its nearest rank over the listings holding the reading, as winners against losers cuts a reading at its
    // deciles, kept at or above it or at or under it; none where no listing holds the reading. Its score and its count are
    // the mean edge of the listings it keeps and how many, nought over none.
    public static ConditionFound? Condition(IdeaLabel label, IReadOnlyList<ConditionUnit> units)
    {
        var reading = ColumnAt(label.Reading);
        var values = units.Where(unit => unit.Readings[reading] is not null).Select(unit => unit.Readings[reading]!.Value).Order().ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        var condition = new AlsoRequires(reading, label.Above, ConditionSearch.Quantile(values, label.Tenth / 10.0));
        var kept = units.Where(unit => condition.Holds(unit.Readings)).ToArray();

        return new ConditionFound([condition], kept.Length == 0 ? 0 : kept.Average(unit => unit.Edge), kept.Length);
    }

    // A label in words, as a proposal's finding states what the model answered.
    public static string Words(IdeaLabel label) =>
        FormattableString.Invariant($"{label.Reading} {(label.Above ? "above" : "below")} the upper edge of its tenth {label.Tenth}");

    static int ColumnAt(string column)
    {
        for (var at = 0; at < LedgerReadings.All.Count; at++)
        {
            if (string.Equals(LedgerReadings.All[at].Column, column, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }

    static string Figure(double? value) => value is { } figure ? figure.ToString("0.###", CultureInfo.InvariantCulture) : "none";
}
