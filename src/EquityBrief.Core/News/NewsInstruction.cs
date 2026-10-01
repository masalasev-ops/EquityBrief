using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.News;

// What a label answered, as code read it: a kind from the nine, a direction for the company from the three,
// and a reason of one sentence holding no digit.
public sealed record NewsLabel(string Kind, string Direction, string Reason);

// The instruction a paid model labels one news article under, written once and versioned, so a label
// records the instruction it was given and a changed instruction is a new version that labels again. It
// asks for three things and nothing else: the kind of story, which way it cuts for the company, and one
// sentence saying why with no figure in it, since a reason is prose and every number in prose must exist in
// the facts file, which a reason's number never would.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
public static class NewsInstruction
{
    public const int Version = 1;

    // The lane and the section a label's call is recorded under, which the feeds read to shape the answer.
    public const string Lane = "news";
    public const string Section = "news label";

    public static bool IsLabel(string section) => string.Equals(section, Section, StringComparison.Ordinal);

    // The nine kinds of story, the brief's words.
    public static readonly string[] Kinds =
    [
        "results",
        "guidance",
        "product",
        "legal or regulatory",
        "deal",
        "analyst change",
        "management",
        "opinion or promotional",
        "other",
    ];

    // The kind whose direction is someone's view rather than an event, which the pages set apart.
    public const string Opinion = "opinion or promotional";

    public static readonly string[] Directions = ["positive", "negative", "neutral"];

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

    public const string System =
        "You label one news article about a listed company for a reader deciding whether to hold a swing trade in it. " +
        "Answer with a JSON object holding exactly three fields and nothing else. " +
        "\"kind\" is one of: results, guidance, product, legal or regulatory, deal, analyst change, management, opinion or promotional, other. " +
        "\"direction\" is what the story means for the company, one of: positive, negative, neutral. " +
        "\"reason\" is one plain sentence saying why, in words alone: no figure, no date, no percentage and no digit of any kind, " +
        "since the reader checks every number elsewhere. Say what the article reports and never what to buy or sell.";

    // The prompt for one article: the company and its ticker, the title and the stored text.
    public static string Prompt(string company, string ticker, string title, string text) =>
        $"Company: {company} ({ticker})\nTitle: {title}\n\nArticle:\n{text}";

    // The schema a Claude profile is asked to answer in, and the one JSON mode is told in words.
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Kinds.Select(kind => (JsonNode?)kind)]) },
            ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Directions.Select(direction => (JsonNode?)direction)]) },
            ["reason"] = new JsonObject { ["type"] = "string" },
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
            var trimmed = text.Trim();
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
        var reason = answer["reason"]?.GetValue<string>()?.Trim();

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
