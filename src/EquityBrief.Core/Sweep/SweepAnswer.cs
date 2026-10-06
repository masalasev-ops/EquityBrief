using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityBrief.Core.Sweep;

// What a sweep run found, as the run itself states it in its folder: the index it read, the family as the cards name it,
// the design where the family sweeps more than one, and whether a setting it read meets the floors, its proposal or a
// setting its second stage crossed. The sweep's own code decides it, at the point its report says whether one passed, and
// a command records it afterwards; nothing reads the floors a second time.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public sealed record SweepAnswer(
    [property: JsonPropertyName("index")] string Index,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("design")] string? Design,
    [property: JsonPropertyName("passed")] bool Passed)
{
    // The file a run states its answer in, beside its report and its figures.
    public const string File = "answer.json";

    public const string PassedWord = "passed";

    public const string NonePassedWord = "none passed";

    // The one line a card draws while the newest answer of each of its family's designs says none passed.
    public const string Line = "Its sweep found no setting that passed the floors";

    public string Word => Passed ? PassedWord : NonePassedWord;

    public string Json() => JsonSerializer.Serialize(this);

    public static SweepAnswer? Read(string json) => JsonSerializer.Deserialize<SweepAnswer>(json);
}

// One answer as the store holds it once recorded: the run it came from and the instant it was recorded.
public sealed record RecordedAnswer(string Run, string Index, string Family, string? Design, string Answer, DateTimeOffset RecordedAt);

// Whether a card draws the line, read from the answers recorded for its family on its index and the instant its live rule
// was registered: drawn where every design's newest answer says none passed, and gone with the family's next sweep that
// passes or its next freeze, a live rule registered after the newest of those answers.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public static class SweepLine
{
    public static bool Drawn(IReadOnlyList<RecordedAnswer> answers, DateTimeOffset? frozenAt)
    {
        var newest = answers
            .GroupBy(answer => answer.Design ?? string.Empty, StringComparer.Ordinal)
            .Select(design => design.OrderByDescending(answer => answer.RecordedAt).ThenByDescending(answer => answer.Run, StringComparer.Ordinal).First())
            .ToArray();

        return newest.Length > 0
            && newest.All(answer => string.Equals(answer.Answer, SweepAnswer.NonePassedWord, StringComparison.Ordinal))
            && (frozenAt is not { } frozen || frozen < newest.Max(answer => answer.RecordedAt));
    }
}
