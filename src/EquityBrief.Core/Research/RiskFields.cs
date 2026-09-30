using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// One risk as the risks section returns it: the risk and the listed document it rests on, what would be seen
// if it came true, and why that means it is coming true with the document that says so. What confirms it is
// a listed fact's name with the way it moves and the level it crosses, or, where no listed fact could show
// it, an event in words with one kind of the seven.
public sealed record RiskField(
    string Risk,
    int RiskDocument,
    string? Fact,
    string? Direction,
    string? Level,
    string? Event,
    string? Kind,
    string Why,
    int WhyDocument)
{
    public bool IsEvent => Fact is null;
}

// The risks section as fields: read from a model's answer, composed into the prose the page reads its parts
// by, stored beside it, and checked for what prose alone could not be: that each fact is one the facts file
// lists, each level one it holds, each kind one of the seven, and that no two risks rest on one fact or one
// kind of event, since three risks confirmed by one miss of guidance cannot be told apart.
// see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
// see: Code owns every number
public static class RiskFields
{
    public const string Section = "The risks, each with what would confirm it";

    public const string RisesAbove = "rises above";
    public const string FallsBelow = "falls below";

    public static readonly string[] Directions = [RisesAbove, FallsBelow];

    // The seven kinds an event risk is given, fixed so that two risks of one kind are told apart by code.
    public static readonly string[] Kinds = ["legal", "regulatory", "competitive", "acquisition", "management", "supply", "other"];

    public const string NotFields = "a risks answer not returned as its fields";
    public const string FactNotListed = "a risk confirmed by a fact the facts file does not list";
    public const string DirectionNotNamed = "a risk's direction that is neither rises above nor falls below";
    public const string FactWithoutLevel = "a risk confirmed by a fact with no direction or no level";
    public const string LevelNotHeld = "a risk's level that is not a figure the facts file holds";
    public const string KindNotListed = "an event risk of a kind outside the seven";
    public const string EventWithLevel = "an event risk carrying a direction or a level";
    public const string SharedFact = "two risks confirmed by the same fact";
    public const string SharedKind = "two risks confirmed by events of one kind";

    static readonly string[] Ordinals = ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];

    static readonly Regex Marker = new(@"\s*\[D\d+\]", RegexOptions.Compiled);

    const string Because = "because ";

    static readonly Regex Fence = new(@"^```(?:json)?\s*|\s*```$", RegexOptions.Compiled);

    public static bool IsRisks(string section) => string.Equals(section, Section, StringComparison.Ordinal);

    // The fields an answer holds, or none where it is not the shape asked for: an object holding a list of
    // risks, each with its risk, its document, what confirms it and why with that document. A fence a writer
    // wrapped the object in is taken off first.
    public static IReadOnlyList<RiskField>? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(Fence.Replace(text.Trim(), string.Empty));
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject whole || whole["risks"] is not JsonArray listed || listed.Count == 0)
        {
            return null;
        }

        var risks = new List<RiskField>();

        foreach (var item in listed)
        {
            if (item is not JsonObject risk
                || Words(risk["risk"]) is not { } said
                || Number(risk["riskDocument"]) is not { } saidIn
                || Words(risk["why"]) is not { } why
                || Number(risk["whyDocument"]) is not { } whyIn
                || risk["confirm"] is not JsonObject confirm)
            {
                return null;
            }

            var fact = Words(confirm["fact"]);
            var happening = Words(confirm["event"]);

            if ((fact is null) == (happening is null))
            {
                return null;
            }

            // A writer set the direction and the level inside what confirms the risk as often as beside it, and
            // opened why on the word the composed sentence already carries, so both are read either way.
            risks.Add(new RiskField(
                said,
                saidIn,
                fact,
                (Words(risk["direction"]) ?? Words(confirm["direction"]))?.ToLowerInvariant(),
                Words(risk["level"]) ?? Words(confirm["level"]),
                happening,
                Words(confirm["kind"])?.ToLowerInvariant(),
                why.StartsWith(Because, StringComparison.OrdinalIgnoreCase) ? why[Because.Length..] : why,
                whyIn));
        }

        return risks;
    }

    // The prose the page reads the risks by: a paragraph each, the risk in a sentence citing its document and
    // what would confirm it in a second citing the document that says why.
    public static string Compose(IReadOnlyList<RiskField> risks) =>
        string.Join("\n\n", risks.Select((risk, at) =>
            "The " + (at < Ordinals.Length ? Ordinals[at] : "next") + " risk is " + risk.Risk + Cite(risk.RiskDocument) + ". "
            + ConfirmedBy(risk) + ", because " + risk.Why + Cite(risk.WhyDocument) + "."));

    // What would confirm a risk, as its second sentence opens.
    public static string ConfirmedBy(RiskField risk) =>
        "That risk would be confirmed by "
        + (risk.IsEvent
            ? risk.Event + ", " + Article(risk.Kind ?? "other") + " " + (risk.Kind ?? "other") + " event"
            : risk.Fact + " " + risk.Direction + " " + risk.Level);

    // The fields as the row stores them, in the order written.
    public static string Stored(IReadOnlyList<RiskField> risks) =>
        new JsonObject
        {
            ["risks"] = new JsonArray([.. risks.Select(risk => (JsonNode)Node(risk))]),
        }.ToJsonString();

    // An answer as a writer stores it: the prose composed from its fields with the fields beside it, or the
    // answer as it came with none, which the checker refuses by name.
    public static (string Prose, string? Parts) FromAnswer(string answer) =>
        Read(answer) is { } risks ? (Compose(risks), Stored(risks)) : (answer, null);

    // What the fields carry that prose alone could not: each finding with the sentence it stands in.
    public static IReadOnlyList<ClaimFinding> Check(string? parts, IReadOnlyList<Fact> facts)
    {
        if (Read(parts) is not { } risks)
        {
            return [new ClaimFinding(string.Empty, "the answer", NotFields)];
        }

        var findings = new List<ClaimFinding>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var risk in risks)
        {
            var sentence = ConfirmedBy(risk);

            if (risk.IsEvent)
            {
                if (risk.Kind is null || !Kinds.Contains(risk.Kind, StringComparer.Ordinal))
                {
                    findings.Add(new ClaimFinding(sentence, risk.Kind ?? "no kind", KindNotListed));
                }
                else if (!kinds.Add(risk.Kind))
                {
                    findings.Add(new ClaimFinding(sentence, risk.Kind, SharedKind));
                }

                if (risk.Direction is not null || risk.Level is not null)
                {
                    findings.Add(new ClaimFinding(sentence, risk.Level ?? risk.Direction!, EventWithLevel));
                }

                continue;
            }

            var fact = risk.Fact!;

            if (!facts.Any(listed => string.Equals(listed.Name, fact, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new ClaimFinding(sentence, fact, FactNotListed));
            }
            else if (!named.Add(fact))
            {
                findings.Add(new ClaimFinding(sentence, fact, SharedFact));
            }

            if (risk.Direction is null || risk.Level is null)
            {
                findings.Add(new ClaimFinding(sentence, fact, FactWithoutLevel));

                continue;
            }

            if (!Directions.Contains(risk.Direction, StringComparer.Ordinal))
            {
                findings.Add(new ClaimFinding(sentence, risk.Direction, DirectionNotNamed));
            }

            if (!HeldLevel(risk.Level, facts))
            {
                findings.Add(new ClaimFinding(sentence, risk.Level, LevelNotHeld));
            }
        }

        return findings;
    }

    // The fact names and levels the fields name, longest first, which the number rule reads past in the
    // composed prose: a listed fact's name may carry digits, and a level is held by its own rule.
    public static IReadOnlyList<string> NamedByFields(string? parts) =>
        Read(parts) is { } risks
            ? [.. risks.SelectMany(risk => new[] { risk.Fact, risk.Level }).OfType<string>().Distinct(StringComparer.Ordinal).OrderByDescending(text => text.Length)]
            : [];

    // A level is one figure, read by the number rule, that the facts file holds.
    static bool HeldLevel(string level, IReadOnlyList<Fact> facts) =>
        ClaimRules.Figures(level) is [{ Kind: FigureKind.Figure } figure] && ClaimRules.Matches(figure, facts);

    static JsonObject Node(RiskField risk)
    {
        var confirm = risk.IsEvent
            ? new JsonObject { ["event"] = risk.Event, ["kind"] = risk.Kind }
            : new JsonObject { ["fact"] = risk.Fact };

        var node = new JsonObject
        {
            ["risk"] = risk.Risk,
            ["riskDocument"] = risk.RiskDocument,
            ["confirm"] = confirm,
        };

        if (risk.Direction is not null)
        {
            node["direction"] = risk.Direction;
        }

        if (risk.Level is not null)
        {
            node["level"] = risk.Level;
        }

        node["why"] = risk.Why;
        node["whyDocument"] = risk.WhyDocument;

        return node;
    }

    static string Cite(int document) => " [D" + document.ToString(CultureInfo.InvariantCulture) + "]";

    static string Article(string word) => "aeiou".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    // A field's words with any marker the writer put in them taken out, and the stop that would close the
    // sentence early, or none where the field is empty or not words.
    static string? Words(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        var said = AnswerText.Visible(Marker.Replace(text, string.Empty)).TrimEnd('.', ';', ',', ' ');

        return said.Length == 0 ? null : said;
    }

    static int? Number(JsonNode? node) =>
        node is not JsonValue value
            ? null
            : value.TryGetValue<int>(out var number)
                ? number
                : value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
