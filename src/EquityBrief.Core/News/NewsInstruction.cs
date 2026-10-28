using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.News;

// What a label answered, as code read it: a kind's key from the nine, a direction for the company from the three,
// and a reason of one sentence holding no digit.
public sealed record NewsLabel(string Kind, string Direction, string Reason);

// The instruction a model labels one news article under, written once and versioned, so a label records the
// instruction it was given and a changed instruction is a new version that labels again. It asks for three things
// and nothing else: the kind of story, which way it cuts for the company, and one sentence saying why with no figure
// in it, since a reason is prose and every number in prose must exist in the facts file, which a reason's number never
// would. From version 2 a kind is answered as a one-word key, each given one line saying what it covers and what it
// does not, its readable name kept for the pages and mapped in code, and the answer is held to the keys and the
// reason to no digit by the schema the local model answers in, so no answer outside them comes back.
// see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
public static class NewsInstruction
{
    public const int Version = 2;

    // The version the paid labeller wrote every one of its labels under, the last before the keys, which a comparison
    // against the paid labels reads them under.
    public const int PaidVersion = 1;

    // The lane and the section a label's call is recorded under, which the feeds read to shape the answer.
    public const string Lane = "news";
    public const string Section = "news label";

    public static bool IsLabel(string section) => string.Equals(section, Section, StringComparison.Ordinal);

    // The nine kinds of story as the keys a model answers with, each with its readable name, the brief's words the
    // pages draw, and the line the instruction gives it: what it covers and what it does not.
    public static readonly (string Key, string Name, string Covers)[] KindsWritten =
    [
        ("results", "results", "a quarter's or a year's reported results, not a forecast of them"),
        ("guidance", "guidance", "the company's own outlook for a coming period, not results already reported"),
        ("product", "product", "a product, service, launch, customer contract or approval, not a deal for a business"),
        ("legal", "legal or regulatory", "a lawsuit, regulator, investigation or fine, not an ordinary business contract"),
        ("deal", "deal", "a merger, acquisition, sale of a business, stake or partnership, not a product contract"),
        ("analyst", "analyst change", "an analyst changing a rating, price target or estimate, not a writer's own view"),
        ("management", "management", "a change among the company's executives or directors, not what they said about results"),
        ("opinion", "opinion or promotional", "commentary, promotion or a recommendation, not a report of something that happened"),
        ("other", "other", "a reported event none of the keys above covers, not commentary"),
    ];

    // The keys a kind is answered with.
    public static readonly string[] Kinds = [.. KindsWritten.Select(kind => kind.Key)];

    // The kind whose direction is someone's view rather than an event, which the pages set apart.
    public const string Opinion = "opinion";

    // A stored kind as its key, one to one: version 1's labels stored the readable names, and a key stands as itself.
    public static string KeyOf(string kind) =>
        KindsWritten.FirstOrDefault(written => string.Equals(written.Name, kind, StringComparison.Ordinal)) is { Key: { } key } ? key : kind;

    // A stored kind by the readable name the pages draw.
    public static string NameOf(string kind) =>
        KindsWritten.FirstOrDefault(written => string.Equals(written.Key, KeyOf(kind), StringComparison.Ordinal)) is { Name: { } name } ? name : kind;

    public static bool IsOpinion(string? kind) => kind is not null && string.Equals(KeyOf(kind), Opinion, StringComparison.Ordinal);

    // The opinion kind as either version stored it, which a query leaves out by.
    public static readonly string[] OpinionStored = [Opinion, NameOf(Opinion)];

    public static readonly string[] Directions = ["positive", "negative", "neutral"];

    // The reason as the schema holds it: no digit, and nothing more, with one sentence read by code. A pattern that also
    // bounds the length with line breaks excluded makes the runtime refuse answers as outside the model's format, and
    // one that also forbids a stop before the last cuts answers off at the budget.
    public const string ReasonPattern = "^[^0-9]*$";

    // The causes an answer is kept as unreadable with, correction 2's seven.
    public const string NotJson = "not JSON";
    public const string KindOutsideTheSet = "a kind outside the set";
    public const string DirectionOutsideTheSet = "a direction outside the set";
    public const string DigitInTheReason = "a digit in the reason";
    public const string NotOneSentence = "not one sentence";
    public const string CutOff = "cut off at the budget";
    public const string Refused = "refused by the model";

    public static readonly string[] Causes = [NotJson, KindOutsideTheSet, DirectionOutsideTheSet, DigitInTheReason, NotOneSentence, CutOff, Refused];

    // How many characters of an article's text are stored and sent.
    public const int TextCharacters = 8000;

    public static readonly string System =
        "You label one news article about a listed company for a reader deciding whether to hold a swing trade in it. " +
        "Answer with a JSON object holding exactly three fields and nothing else. " +
        "\"kind\" is exactly one of these keys, each with what it covers and what it does not: " +
        string.Join(" ", KindsWritten.Select(kind => kind.Key + ": " + kind.Covers + ".")) + " " +
        "\"direction\" is what the story means for the company, one of: positive, negative, neutral. " +
        "\"reason\" is one plain sentence saying why, in words alone: no figure, no date, no percentage and no digit of any kind, " +
        "since the reader checks every number elsewhere. Say what the article reports and never what to buy or sell.";

    // The prompt for one article: the company and its ticker, the title and the stored text.
    public static string Prompt(string company, string ticker, string title, string text) =>
        $"Company: {company} ({ticker})\nTitle: {title}\n\nArticle:\n{text}";

    // The schema the answer is held to: the keys and the directions as fixed lists and the reason to its pattern.
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Kinds.Select(kind => (JsonNode?)kind)]) },
            ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Directions.Select(direction => (JsonNode?)direction)]) },
            ["reason"] = new JsonObject { ["type"] = "string", ["pattern"] = ReasonPattern },
        },
        ["required"] = new JsonArray("kind", "direction", "reason"),
        ["additionalProperties"] = false,
    };

    // The answer read by code: a label, or the cause it cannot be read. Every check runs on the text the
    // model answered, whichever profile answered it, so a profile whose wire enforces no schema is held to
    // the same three fields.
    public static (NewsLabel? Label, string? Cause) Read(string text)
    {
        JsonObject? answer;

        try
        {
            // A line break inside a string is not JSON and between its parts is only space, so each is read as a space:
            // a model that breaks its reason's line still answers one sentence.
            var trimmed = text.Trim().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            var start = trimmed.IndexOf('{', StringComparison.Ordinal);
            var end = trimmed.LastIndexOf('}');

            answer = start >= 0 && end > start ? JsonNode.Parse(trimmed[start..(end + 1)]) as JsonObject : null;
        }
        catch (JsonException)
        {
            answer = null;
        }

        if (answer is null)
        {
            return (null, NotJson);
        }

        var kind = answer["kind"]?.GetValue<string>()?.Trim().ToLowerInvariant();
        var direction = answer["direction"]?.GetValue<string>()?.Trim().ToLowerInvariant();
        var reason = answer["reason"]?.GetValue<string>() is { } said ? string.Join(' ', said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : null;

        if (kind is null || !Kinds.Contains(kind, StringComparer.Ordinal))
        {
            return (null, KindOutsideTheSet);
        }

        if (direction is null || !Directions.Contains(direction, StringComparer.Ordinal))
        {
            return (null, DirectionOutsideTheSet);
        }

        if (string.IsNullOrEmpty(reason) || !IsOneSentence(reason))
        {
            return (null, NotOneSentence);
        }

        if (reason.Any(char.IsDigit))
        {
            return (null, DigitInTheReason);
        }

        return (new NewsLabel(kind, direction, reason), null);
    }

    // One sentence: some words, ending on at most one full stop, with no sentence end before it. A stop
    // inside an abbreviation or a decimal would read as a second sentence, and a digit is refused anyway.
    static bool IsOneSentence(string reason)
    {
        var body = reason.TrimEnd('.', '!', '?').TrimEnd();

        if (body.Length == 0 || !body.Any(char.IsLetter))
        {
            return false;
        }

        var ends = reason.Length - reason.TrimEnd('.', '!', '?').Length;

        return ends <= 1 && !body.Contains(". ", StringComparison.Ordinal) && !body.Contains("! ", StringComparison.Ordinal) && !body.Contains("? ", StringComparison.Ordinal) && !body.Contains('\n');
    }
}
