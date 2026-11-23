using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Loop;

// What the local model understood of an idea typed in the box: its line as code wrote it from the answer's closed choices and
// the one sentence saying what it understood; none and why where the words hold no idea the grammar can say, or the answer
// could not be read.
public sealed record IdeaBoxRead(IdeaLine? Line, string? Reason, bool None, string? Cause);

// The idea box's ask, closed: the operator's words put to the local model, which answers the researcher's own closed choices
// with the setup and the index the words are about and one sentence holding no digit saying what it understood. Code writes
// the line from the choices as it writes the researcher's, the setup's own event on its index, its own time limit and its
// five a night, so an idea the box gives is one its setup can run as a variant. Nothing the model answers joins a test until
// the operator confirms the line it was read into.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public static class IdeaBoxPrompt
{
    // The longest an idea typed in the box may be.
    public const int MostCharacters = 1000;

    // An answer's budget, the researcher's own.
    public const int AnswerTokens = ResearchPrompt.AnswerTokens;

    public const string Unknown = "a setup or an index the grammar does not hold";

    public const string Instruction =
        "You turn an idea the operator typed in plain words into one line of a closed grammar, for testing a stock-picking "
        + "rule. Choose the setup the words are about, the pullback, the breakout, the drift or the dip, and the index, the "
        + "S&P 500, 400 or 600. Then choose up to three conditions, each keeping the picks whose reading stands in a band of its "
        + "tenths, the tenths numbered one, the lowest, to ten; how it buys; its stop in typical days under the buy; how it "
        + "takes profit; and the reading a night's picks are ordered by, highest or lowest first. Choose only from the lists "
        + "the schema gives, and where the words name something the lists do not hold, choose the nearest and say so. Give one "
        + "plain sentence saying what you understood, holding no number, no digit, no ticker and no company's name. Reply "
        + "with one JSON object and nothing else, as the schema shapes it, with has_idea false where the words hold no idea "
        + "the grammar can say.";

    // The instruction's version, the first twelve hex characters of the SHA-256 of its bytes.
    public static string Version { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction)))[..12];

    // The researcher's schema with the setup and the index before its choices.
    public static JsonObject Schema()
    {
        static JsonObject Of(IEnumerable<string> values) => new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]) };

        var choices = ResearchPrompt.Schema();
        var properties = new JsonObject
        {
            ["setup"] = Of(IdeaGrammar.Families),
            ["index"] = Of(IdeaGrammar.Indices),
        };

        foreach (var (name, value) in (JsonObject)choices["properties"]!)
        {
            properties[name] = value!.DeepClone();
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. properties.Select(property => (JsonNode)property.Key)]),
            ["additionalProperties"] = false,
        };
    }

    // The prompt the operator's words make: the words as typed, and the readings a condition or an order may name.
    public static string For(string words) =>
        new StringBuilder()
            .AppendLine("The operator's idea, as typed:")
            .AppendLine(words.Trim())
            .AppendLine()
            .Append("The readings a condition or an order may name: ")
            .AppendLine(string.Join(", ", IdeaGrammar.Readings))
            .ToString();

    // The key an answer is stored under: the instruction's version and the prompt, hashed.
    public static string Key(string prompt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + prompt)))[..32];

    // An answer read into its line, or why it cannot be: the setup and the index first, then the researcher's own reading of
    // the choices and the sentence.
    public static IdeaBoxRead Read(string? content, string? finishReason)
    {
        var first = ResearchPrompt.Read(content, finishReason, IdeaGrammar.Families[0], IdeaGrammar.CodeOf(IdeaGrammar.Indices[0]));

        if (first.Cause is { } cause)
        {
            return new IdeaBoxRead(null, null, false, cause);
        }

        if (first.None)
        {
            return new IdeaBoxRead(null, null, true, null);
        }

        using var document = JsonDocument.Parse(content!.Trim());
        var root = document.RootElement;
        var family = root.TryGetProperty("setup", out var setup) && setup.ValueKind == JsonValueKind.String ? setup.GetString() : null;
        var index = root.TryGetProperty("index", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;

        if (family is null || !IdeaGrammar.Families.Contains(family) || index is null || !IdeaGrammar.Indices.Contains(index))
        {
            return new IdeaBoxRead(null, null, false, Unknown);
        }

        var read = ResearchPrompt.Read(content, finishReason, family, IdeaGrammar.CodeOf(index));

        return read.Idea is { } idea ? new IdeaBoxRead(idea.Line, idea.Reason, false, null) : new IdeaBoxRead(null, null, read.None, read.Cause);
    }

    // The causes an answer is asked once more for before it is stored refused: the researcher's and a setup or an index
    // outside the grammar.
    public static IReadOnlyList<string> AnswerCauses { get; } = [.. ResearchPrompt.AnswerCauses, Unknown];

    // The words as the box keeps them: trimmed, and cut at the longest it takes.
    public static string Trimmed(string words) =>
        words.Length <= MostCharacters ? words.Trim() : words[..MostCharacters].Trim();
}
