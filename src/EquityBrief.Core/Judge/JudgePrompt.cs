using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Judge;

// One pick as the model answered it: the label it was shown under, the hold and the take-profit it chose from the menu,
// and its reason.
public sealed record JudgeChoice(string Label, string Hold, string Shape, string Reason);

// The judge's two rounds as the model is asked them. Round one shows the short rows of every member about 50 at a time,
// each under a made-up label drawn by a seeded shuffle, and keeps at most one of each batch; round two shows the kept
// members' full sheets together and picks up to 5, ranked, each with a hold and a take-profit from the menu and a reason
// holding no digit and no name. The model answers closed choices and words alone; code reads every answer and refuses
// one outside them.
// see: The AI judge picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
public static partial class JudgePrompt
{
    public const int BatchSize = 50;

    public const int KeepPerBatch = 1;

    public const int MostPicks = 5;

    public const int ScreenTokens = 64;

    public const int PickTokens = 1024;

    public const string None = "none";

    public const string ReasonPattern = "^[^0-9]*$";

    public const string ScreenSystem =
        "You will read short fact rows about companies listed in the United States, each under a made-up label. Their names, tickers, "
        + "industries, dates and prices have been removed. Choose the one company you would most want to buy at the next open and hold "
        + "for between a week and three months, or answer none if no company stands out. Answer with its label.";

    public static readonly string PickSystem =
        "You will read full fact sheets about companies listed in the United States, each under a made-up label. Their names, tickers, "
        + "industries, dates and prices have been removed. Choose up to " + Words(MostPicks) + " to buy at the next open, ranked with the one "
        + "you most want first, or none. For each, choose how long to hold it and how to take profit from the lists given, and give one "
        + "sentence of reasoning in plain words, with no digit and no company's name.";

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

    public static string ScreenPrompt(IReadOnlyList<(string Label, string Row)> rows) =>
        string.Join("\n", rows.Select(row => row.Label + ": " + row.Row));

    public static string PickPrompt(IReadOnlyList<(string Label, string Sheet)> sheets) =>
        "How long to hold: " + string.Join("; ", JudgeMenu.Holds.Select(hold => hold.Words)) + ".\n"
        + "How to take profit: " + string.Join("; ", JudgeMenu.Shapes) + ".\n\n"
        + string.Join("\n\n", sheets.Select(sheet => sheet.Label + "\n" + sheet.Sheet));

    public static JsonObject ScreenSchema(IReadOnlyList<string> labels) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["keep"] = new JsonObject { ["type"] = "string", ["enum"] = Enum([.. labels, None]) },
        },
        ["required"] = new JsonArray("keep"),
        ["additionalProperties"] = false,
    };

    public static JsonObject PickSchema(IReadOnlyList<string> labels) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["picks"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = MostPicks,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["label"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(labels) },
                        ["hold"] = new JsonObject { ["type"] = "string", ["enum"] = Enum([.. JudgeMenu.Holds.Select(hold => hold.Words)]) },
                        ["take_profit"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(JudgeMenu.Shapes) },
                        ["reason"] = new JsonObject { ["type"] = "string", ["pattern"] = ReasonPattern },
                    },
                    ["required"] = new JsonArray("label", "hold", "take_profit", "reason"),
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
            string.Join(",", JudgeMenu.Shapes), FormattableString.Invariant($"{BatchSize}:{KeepPerBatch}:{MostPicks}:{ScreenTokens}:{PickTokens}:{ReasonPattern}"),
        ]))))[..12];

    // Round one's answer: the label kept, none where the model kept none, or the cause it cannot be read.
    public static (string? Kept, string? Cause) ReadScreen(string? answer, IReadOnlyList<string> labels)
    {
        if (Parse(answer) is not { } root)
        {
            return (null, "not one object");
        }

        if (root["keep"] is not JsonValue value || !value.TryGetValue(out string? keep) || keep is null)
        {
            return (null, "missing an answer");
        }

        if (keep == None)
        {
            return (null, null);
        }

        return labels.Contains(keep, StringComparer.Ordinal) ? (keep, null) : (null, "a label not shown");
    }

    // Round two's answer: up to 5 picks in the order ranked, or the cause it cannot be read. A label shown twice, one not
    // shown, a hold or a take-profit off the menu, or a reason holding a digit is refused whole; a reason naming the member
    // is refused by the caller, which alone holds the names.
    public static (IReadOnlyList<JudgeChoice> Picks, string? Cause) ReadPick(string? answer, IReadOnlyList<string> labels)
    {
        if (Parse(answer) is not { } root)
        {
            return ([], "not one object");
        }

        if (root["picks"] is not JsonArray picks)
        {
            return ([], "missing an answer");
        }

        if (picks.Count > MostPicks)
        {
            return ([], FormattableString.Invariant($"more than {MostPicks} picks"));
        }

        var read = new List<JudgeChoice>();

        foreach (var pick in picks)
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

            read.Add(new JudgeChoice(label, hold, shape, reason.Trim()));
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
        5 => "five",
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    [GeneratedRegex("[0-9]")]
    private static partial Regex Digit();
}
