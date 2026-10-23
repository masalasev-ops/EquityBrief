using System.Text.Json;

namespace EquityBrief.Core.Releases;

// The four words the local model answered for one release, each a word its question lists, and the readings code
// turns them into.
public sealed record ReleaseAnswer(string Results, string Outlook, string Tone, string Demand)
{
    public string Word(string key) => key switch
    {
        ReleaseQuestions.Results => Results,
        ReleaseQuestions.Outlook => Outlook,
        ReleaseQuestions.Tone => Tone,
        ReleaseQuestions.Demand => Demand,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "The release reading asks no such question."),
    };

    // The reading a question's word gives: a whole number from -1 to 1, or none where the word says the release states
    // nothing on it, and never nought for none.
    public int? Reading(string key) => ReleaseQuestions.For(key).Of(Word(key))!.Reading;
}

// An answer as code read it: the four words, or the cause it could not be stored as a reading.
public sealed record ReleaseAnswerRead(ReleaseAnswer? Answer, string? Cause);

// Reading the local model's answer for a release into the four words, and the causes an answer is refused for. An
// answer that cannot be read is asked once more and then refused with its cause, never read as a reading.
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
public static class ReleaseAnswers
{
    // The causes an answer is refused for: the first five read off the answer, the next two off the runtime, and the
    // last an answer the pin's model did not give.
    public const string Empty = "empty";
    public const string CutOff = "cut off";
    public const string NotOneObject = "not one object";
    public const string MissingAnAnswer = "missing an answer";
    public const string WordNotListed = "a word not listed";
    public const string RuntimeRefused = "the runtime refused";
    public const string NotAnsweredInTime = "not answered in time";
    public const string NotThePins = "not the pin's model";

    public static IReadOnlyList<string> Causes { get; } =
        [Empty, CutOff, NotOneObject, MissingAnAnswer, WordNotListed, RuntimeRefused, NotAnsweredInTime, NotThePins];

    // The finish reason a runtime gives an answer that ran out of its budget.
    public const string BudgetReached = "length";

    // The answer's text and the reason it finished, as the native answer carries them. An answer is one object holding
    // each question's key with one of its listed words, written exactly as listed; a key the questions do not ask is
    // passed over.
    public static ReleaseAnswerRead Read(string? content, string? finishReason)
    {
        if (string.Equals(finishReason, BudgetReached, StringComparison.Ordinal))
        {
            return new(null, CutOff);
        }

        var text = (content ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new(null, Empty);
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(null, NotOneObject);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(null, NotOneObject);
        }

        var words = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var question in ReleaseQuestions.All)
        {
            if (!root.TryGetProperty(question.Key, out var given) || given.ValueKind != JsonValueKind.String)
            {
                return new(null, MissingAnAnswer);
            }

            var word = given.GetString()!.Trim();

            if (question.Of(word) is null)
            {
                return new(null, WordNotListed);
            }

            words[question.Key] = word;
        }

        return new(
            new ReleaseAnswer(words[ReleaseQuestions.Results], words[ReleaseQuestions.Outlook], words[ReleaseQuestions.Tone], words[ReleaseQuestions.Demand]),
            null);
    }
}
