using System.Security.Cryptography;
using System.Text;

namespace EquityBrief.Core.Releases;

// One word a question lists, and the reading code turns it into: a whole number from -1 to 1, or none where the word
// says the release states nothing on it.
public sealed record ReleaseWord(string Word, int? Reading);

// One closed question the local model answers of a results release: the key its answer carries, what it asks and the
// words it may answer with, each turned into a reading by code.
public sealed record ReleaseQuestion(string Key, string Asks, IReadOnlyList<ReleaseWord> Words)
{
    // The word as the question lists it, or none where the question lists no such word.
    public ReleaseWord? Of(string word) =>
        Words.FirstOrDefault(listed => string.Equals(listed.Word, word, StringComparison.Ordinal));
}

// The four questions the release reading asks, written before any release was read and never tuned on what the
// answers show, the instruction the local model is given from them, and the version that instruction is pinned by.
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
public static class ReleaseQuestions
{
    public const string Results = "results";
    public const string Outlook = "outlook";
    public const string Tone = "tone";
    public const string Demand = "demand";

    public static IReadOnlyList<ReleaseQuestion> All { get; } =
    [
        new(Results, "how the release describes the quarter against what was expected",
            [new("better", 1), new("in line", 0), new("worse", -1), new("not stated", null)]),
        new(Outlook, "what the release does to the outlook",
            [new("raised", 1), new("kept", 0), new("lowered or withdrawn", -1), new("none given", null)]),
        new(Tone, "the tone of management's own words",
            [new("confident", 1), new("neutral", 0), new("cautious", -1)]),
        new(Demand, "how the release describes demand or orders",
            [new("stronger", 1), new("steady", 0), new("weaker", -1), new("not stated", null)]),
    ];

    public static ReleaseQuestion For(string key) =>
        All.First(question => string.Equals(question.Key, key, StringComparison.Ordinal));

    // The instruction, written from the questions so the words the model is asked for are the words code reads: what
    // it reads, that it answers from the text alone, the one object it replies with, and each key with its words.
    public static string Instruction { get; } =
        "You read a company's quarterly results release. Answer four questions about what the release itself says, " +
        "from its text alone. Reply with one JSON object and nothing else, with exactly these four keys, each holding " +
        "exactly one of its listed words: " +
        string.Join("; ", All.Select(question => $"\"{question.Key}\": {Listed(question)}, for {question.Asks}")) +
        ".";

    // A question's words as the instruction lists them: each quoted, the last after "or".
    static string Listed(ReleaseQuestion question)
    {
        var quoted = question.Words.Select(word => $"\"{word.Word}\"").ToArray();

        return string.Join(", ", quoted[..^1]) + " or " + quoted[^1];
    }

    // The prompt's version: the first twelve hex characters of the SHA-256 of the instruction's bytes and, from 20.3, the
    // anonymiser's own version after it on a line of its own, so a word, a question or a rule of what a release is read
    // without moved is another version, which a pin refuses until a new one is written; from the 20.3 correction of
    // 2026-10-22 the fit's rule on a line after them, so a release cut to the window is read under a version of its own.
    public static string Version { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction + "\n" + ReleaseAnonymiser.Version + "\n" + ReleaseFit.Version)))[..12];

    // The version as every reading before the fit's rule was asked under, the instruction and the anonymiser alone: a pin
    // whose version this is asked each release it read with the instruction and the anonymiser of today.
    public static string VersionBeforeTheFit { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction + "\n" + ReleaseAnonymiser.Version)))[..12];
}
