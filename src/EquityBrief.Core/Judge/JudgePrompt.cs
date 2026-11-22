using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Judge;

// One pick as the model answered it: the label it was shown under, the hold and the take-profit it chose from the menu,
// its reason, and from 24.14 the facts of its sheet it named as deciding it, each by the words its sheet shows.
public sealed record JudgeChoice(string Label, string Hold, string Shape, string Reason)
{
    public IReadOnlyList<string> Fields { get; init; } = [];
}

// The picker's two rounds as the model is asked them, from 24.24 within one index at a time. Round one shows the short
// rows of that index's members 50 at a time, each under a made-up label drawn by a seeded shuffle and each figure with the
// tenth it stands in among that index's stocks, and marks every member it rates worth buying, each judged on its own
// facts, the batch only the size the model reads in; round two shows the marked members' full sheets at most 16 at a
// time and ranks the best of them, the best quarter of the marked rounded up and at most 5, each with a hold and a
// take-profit from the menu and a reason holding no digit and no name. The model answers closed choices and words alone;
// code reads every answer and refuses one outside them.
// see: The AI stock picker picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public static partial class JudgePrompt
{
    public const int BatchSize = 50;

    // From 24.24 the most marked members' sheets one question of round two shows, the most the local model answered every
    // question of over replayed nights, and the share of the marked it picks: one in four, rounded up.
    public const int SheetsPerAsk = 16;

    public const int QuarterOf = 4;

    public const int MostPicks = 5;

    public const int ScreenTokens = 640;

    // From 24.24 the answer's budget of round two: five ranked picks with their facts, which the model writes laid out over
    // more than a thousand tokens.
    public const int PickTokens = 2048;

    public const string None = "none";

    public const string ReasonPattern = "^[^0-9]*$";

    // From 24.14 how many facts of its sheet a pick names as deciding it.
    public const int FewestFields = 3;

    public const int MostFields = 5;

    // The prompt versions a picker pin may move from to this one, each followed by a new pin of its own: the version the
    // picker picked under before it named its facts, and the one it named them under before it picked within each index.
    // Any other version a pin holds is refused.
    public static IReadOnlyList<string> Earlier { get; } = ["837506398bd8", "407c9a9b3be1"];

    // How many of the marked the picker picks: the best quarter, rounded up, at most 5, and none where none is marked.
    public static int Quarter(int marked) => marked <= 0 ? 0 : Math.Min(MostPicks, (marked + QuarterOf - 1) / QuarterOf);

    // How many of the marked round two ranks: as many as a fixed 5 takes, at most 5, the best quarter the first of them.
    public static int Ranked(int marked) => marked <= 0 ? 0 : Math.Min(MostPicks, marked);

    public const string NamedNone = "it named no fact";

    public const string NamedTooFewOrMany = "it named fewer than three facts or more than five";

    public const string NamedTwice = "it named a fact twice";

    public const string NamedLacking = "it named a fact its sheet lacks";

    public const string ScreenSystem =
        "You will read short fact rows about companies in one index of stocks listed in the United States, each under a made-up label. "
        + "Their names, tickers, industries, dates and prices have been removed, and each figure is followed by the tenth it stands in "
        + "among that index's stocks, from 1 the lowest to 10 the highest. Judge each company on its own facts, and mark every one you "
        + "rate worth buying at the next open and holding for between a week and three months, or none if none is. Answer with their labels.";

    public static readonly string PickSystem =
        "You will read full fact sheets about companies in one index of stocks listed in the United States, each under a made-up label "
        + "and each already rated worth buying. Their names, tickers, industries, dates and prices have been removed, and each figure is "
        + "followed by the tenth it stands in among that index's stocks, from 1 the lowest to 10 the highest. Rank the number of them asked "
        + "that you most want to buy at the next open, the one you most want first. For each, choose how long to hold it and how to take "
        + "profit from the lists given, give one sentence of reasoning in plain words, with no digit and no company's name, and name the "
        + Words(FewestFields) + " to " + Words(MostFields) + " facts of its sheet that decided it, each by its name exactly as the sheet shows it.";

    // The line opening each question, naming the index its members are placed among.
    public static string AmongLine(string indexName) => $"Every company here is in the {indexName}, and each tenth places a figure among the {indexName}'s stocks tonight.";

    static readonly string[] Colours = ["Amber", "Blue", "Coral", "Gray", "Green", "Ivory", "Olive", "Violet"];

    static readonly string[] Things = ["Bell", "Cloud", "Field", "Kite", "Lamp", "Leaf", "River", "Stone"];

    // Every label a batch may be shown under, 64 in all, none a word a ticker or a figure could be read from.
    public static IReadOnlyList<string> AllLabels { get; } = [.. Colours.SelectMany(colour => Things.Select(thing => colour + " " + thing))];

    // The labels of one ask, the first `count` of every label shuffled by the seed.
    public static IReadOnlyList<string> Labels(int seed, int count)
    {
        var shuffled = AllLabels.ToArray();
        var random = new Random(seed);

        for (var at = shuffled.Length - 1; at > 0; at--)
        {
            var other = random.Next(at + 1);

            (shuffled[at], shuffled[other]) = (shuffled[other], shuffled[at]);
        }

        return count <= shuffled.Length
            ? [.. shuffled.Take(count)]
            : throw new ArgumentOutOfRangeException(nameof(count), FormattableString.Invariant($"an ask shows at most {shuffled.Length} members"));
    }

    public static string ScreenPrompt(string indexName, IReadOnlyList<(string Label, string Row)> rows) =>
        AmongLine(indexName) + "\n\n" + string.Join("\n", rows.Select(row => row.Label + ": " + row.Row));

    public static string PickPrompt(string indexName, int count, IReadOnlyList<(string Label, string Sheet)> sheets) =>
        AmongLine(indexName) + "\n"
        + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Rank {count} of these {sheets.Count}.\n")
        + "How long to hold: " + string.Join("; ", JudgeMenu.Holds.Select(hold => hold.Words)) + ".\n"
        + "How to take profit: " + string.Join("; ", JudgeMenu.Shapes) + ".\n\n"
        + string.Join("\n\n", sheets.Select(sheet => sheet.Label + "\n" + sheet.Sheet));

    public static JsonObject ScreenSchema(IReadOnlyList<string> labels) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["marked"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = labels.Count,
                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(labels) },
            },
        },
        ["required"] = new JsonArray("marked"),
        ["additionalProperties"] = false,
    };

    public static JsonObject PickSchema(IReadOnlyList<string> labels, int count) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["picks"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = count,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["label"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(labels) },
                        ["hold"] = new JsonObject { ["type"] = "string", ["enum"] = Enum([.. JudgeMenu.Holds.Select(hold => hold.Words)]) },
                        ["take_profit"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(JudgeMenu.Shapes) },
                        ["reason"] = new JsonObject { ["type"] = "string", ["pattern"] = ReasonPattern },
                        ["facts"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                        },
                    },
                    ["required"] = new JsonArray("label", "hold", "take_profit", "reason", "facts"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("picks"),
        ["additionalProperties"] = false,
    };

    // The prompts' version: a hash over both instructions, the label words, the menu and the bounds, so any change to what
    // the model is asked is a new version its pin refuses.
    public static string Version { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
        "\n",
        [
            ScreenSystem, PickSystem, string.Join(",", AllLabels), string.Join(",", JudgeMenu.Holds.Select(hold => FormattableString.Invariant($"{hold.Words}:{hold.Sessions}:{hold.StopMoves}"))),
            string.Join(",", JudgeMenu.Shapes), FormattableString.Invariant($"{BatchSize}:{SheetsPerAsk}:{QuarterOf}:{MostPicks}:{ScreenTokens}:{PickTokens}:{ReasonPattern}"),
            AmongLine("x"),
            string.Join(",", FieldWords), FormattableString.Invariant($"{FewestFields}:{MostFields}"),
        ]))))[..12];

    // The facts a pick may name: every field of the full sheet, by the words the sheet shows it under.
    public static IReadOnlyList<string> FieldWords => [.. JudgeFacts.In(JudgeRound.Sheet).Select(shown => shown.Words)];

    // The words of the field a fact is named by: the field's own words, or those before its value where it is named as its
    // sheet's line shows it; the words as given where they name no field.
    public static string NameOf(string named) =>
        FieldWords.FirstOrDefault(words => string.Equals(named, words, StringComparison.Ordinal) || named.StartsWith(words + ": ", StringComparison.Ordinal)) ?? named;

    // The key of the field a sheet shows under these words, none where no field is.
    public static string? KeyOf(string words) => JudgeFacts.All.FirstOrDefault(shown => string.Equals(shown.Words, words, StringComparison.Ordinal))?.Key;

    // Why the facts a pick named are refused, none where they stand: none named; fewer than three or more than five; one
    // named twice; or one the member's sheet does not show, or shows as not on file.
    public static string? FieldsRefused(IReadOnlyList<string> named, string sheet)
    {
        if (named.Count == 0)
        {
            return NamedNone;
        }

        if (named.Count is < FewestFields or > MostFields)
        {
            return NamedTooFewOrMany;
        }

        if (named.Distinct(StringComparer.Ordinal).Count() != named.Count)
        {
            return NamedTwice;
        }

        var lines = sheet.Split('\n');

        return named.All(words => KeyOf(words) is not null && lines.Any(line => line.StartsWith(words + ": ", StringComparison.Ordinal) && line[(words.Length + 2)..] != JudgeFacts.NotOnFile))
            ? null
            : NamedLacking;
    }

    // Round one's answer: the labels marked, in the order shown, none where the model marked none, a label marked twice read
    // once, or the cause it cannot be read: a label not shown.
    public static (IReadOnlyList<string> Marked, string? Cause) ReadScreen(string? answer, IReadOnlyList<string> labels)
    {
        if (Parse(answer) is not { } root)
        {
            return ([], "not one object");
        }

        if (root["marked"] is not JsonArray marked)
        {
            return ([], "missing an answer");
        }

        var read = new List<string>();

        foreach (var one in marked)
        {
            if (one is not JsonValue value || !value.TryGetValue(out string? label) || label is null)
            {
                return ([], "missing an answer");
            }

            if (!labels.Contains(label, StringComparer.Ordinal))
            {
                return ([], "a label not shown");
            }

            // A label marked twice marks its member once.
            if (!read.Contains(label, StringComparer.Ordinal))
            {
                read.Add(label);
            }
        }

        return ([.. labels.Where(label => read.Contains(label, StringComparer.Ordinal))], null);
    }

    // Round two's answer: the number of picks asked in the order ranked, the first of them where it ranks more, or the
    // cause it cannot be read. A label shown twice, one not shown, a hold or a take-profit off the menu, a reason holding a
    // digit, or fewer picks than asked is refused whole; a reason naming the member is refused by the caller, which alone
    // holds the names.
    public static (IReadOnlyList<JudgeChoice> Picks, string? Cause) ReadPick(string? answer, IReadOnlyList<string> labels, int count)
    {
        if (Parse(answer) is not { } root)
        {
            return ([], "not one object");
        }

        if (root["picks"] is not JsonArray picks)
        {
            return ([], "missing an answer");
        }

        if (picks.Count < count)
        {
            return ([], FormattableString.Invariant($"{picks.Count} picks where {count} were asked"));
        }

        var ranked = picks.Take(count).ToArray();

        var read = new List<JudgeChoice>();

        foreach (var pick in ranked)
        {
            if (pick is not JsonObject one
                || Text(one, "label") is not { } label
                || Text(one, "hold") is not { } hold
                || Text(one, "take_profit") is not { } shape
                || Text(one, "reason") is not { } reason)
            {
                return ([], "missing an answer");
            }

            if (!labels.Contains(label, StringComparer.Ordinal))
            {
                return ([], "a label not shown");
            }

            if (read.Any(kept => kept.Label == label))
            {
                return ([], "a label picked twice");
            }

            if (JudgeMenu.HoldOf(hold) is null || !JudgeMenu.IsShape(shape))
            {
                return ([], "a choice not on the menu");
            }

            if (string.IsNullOrWhiteSpace(reason) || Digit().IsMatch(reason))
            {
                return ([], "a reason holding a digit or none");
            }

            // The facts it named are read as given, none where it named none; the caller, which holds each sheet, refuses
            // the one pick whose facts do not stand and keeps the rest.
            // From 24.24 a fact named with its value after it, as its sheet shows it, is read by its name.
            IReadOnlyList<string> facts = one["facts"] is JsonArray named
                ? [.. named.Select(fact => fact is JsonValue value && value.TryGetValue(out string? words) ? NameOf(words) : string.Empty)]
                : [];

            read.Add(new JudgeChoice(label, hold, shape, reason.Trim()) { Fields = facts });
        }

        return (read, null);
    }

    // Whether a reason names the member it is given for: its ticker as a word of its own in capitals, where the ticker is
    // three letters or more, since one or two capitals are read in plain sentences, or its name as the naming test reads one.
    public static bool NamesTheMember(string reason, string ticker, string? name) =>
        (ticker.Length >= 3 && Regex.IsMatch(reason, $@"(?<![A-Za-z]){Regex.Escape(ticker)}(?![A-Za-z])", RegexOptions.CultureInvariant))
        || JudgeNaming.Named(reason, null, ticker, name);

    static JsonArray Enum(IEnumerable<string> words) => [.. words.Select(word => (JsonNode?)word)];

    static JsonObject? Parse(string? answer)
    {
        try
        {
            return string.IsNullOrWhiteSpace(answer) ? null : JsonNode.Parse(answer) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    static string Words(int count) => count switch
    {
        3 => "three",
        5 => "five",
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    [GeneratedRegex("[0-9]")]
    private static partial Regex Digit();
}
