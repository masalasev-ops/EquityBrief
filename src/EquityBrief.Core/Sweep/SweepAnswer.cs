using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityBrief.Core.Sweep;

// What a sweep run found, as the run itself states it in its folder: the index it read, the family as the cards name it,
// the design where the family sweeps more than one, and whether a setting it read meets the floors, its proposal or a
// setting its second stage crossed. The sweep's own code decides it, at the point its report says whether one passed, and
// a command records it afterwards; nothing reads the floors a second time.
// see: No family on any index is set aside or hidden by a test result without the operator's word
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
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

    [JsonIgnore]
    public string Word => Passed ? PassedWord : NonePassedWord;

    public string Json() => JsonSerializer.Serialize(this);

    public static SweepAnswer? Read(string json) => JsonSerializer.Deserialize<SweepAnswer>(json);
}

// One answer as the store holds it once recorded: the run it came from and the instant it was recorded, and from 20.11
// the figures its run stated where it stated them.
public sealed record RecordedAnswer(string Run, string Index, string Family, string? Design, string Answer, DateTimeOffset RecordedAt, AnswerFigures? Figures = null);

// The figures a run states for the setting its family would run, as the sweep-answer command records them from the run's
// own figures file: the setting's key, its trades or holdings, its edge after costs, in risks or for the sector
// heavyweights as a fraction of the buy against the size cut, its edge each year by the calendar year it is counted in,
// its edge without its five largest results, how many settings its first stage read and how many met the floors against
// what luck alone would pass, none where the run states no such count, and the tester's verdict on the family's search,
// its run, adjusted p and whether it passed, none where no tester run was recorded with it. Where the operator ruled
// another setting the run read in its place, that setting's figures, the day of the ruling and whether the run's second
// stage kept the entry the setting is bought on.
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
public sealed record AnswerFigures(
    string Setting,
    int Trades,
    double? Edge,
    IReadOnlyDictionary<int, double?> YearEdges,
    double? WithoutLargest,
    int? SettingsRead,
    int? Meeting,
    double? Luck,
    string? TesterRun,
    double? Adjusted,
    bool? TesterPassed,
    DateOnly? RuledOn = null,
    bool? EntryKept = null)
{
    // Whether more of the settings the run read met the floors than luck alone would pass, none where it states no count.
    public bool? BeatLuck => Meeting is { } meeting && Luck is { } luck ? meeting > luck : null;
}

// One freeze of a family on its index: the instant its live rule became a rule other than the one standing just before
// it, and that rule's name. The same rule registered again, as a moved evaluator's is, is no freeze.
public sealed record SweepFreeze(string Rule, DateTimeOffset At);

// Whether a card draws the line, read from the answers recorded for its family on its index and the family's freezes:
// drawn where every design's newest answer says none passed and no live rule reads the card, or no freeze came at or
// after that answer, or the first that did, its freeze, made the live rule standing now and none came after it. A
// family frozen on a sweep that passed none keeps the line, and its next freeze or a newer sweep that passes ends it.
// see: No family on any index is set aside or hidden by a test result without the operator's word
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
public static class SweepLine
{
    // The words a kept line adds where the setting frozen at it lost in the last years its sweep stored, and the number
    // of those years it reads.
    public const string LostWords = ", and the setting frozen here lost in ";

    public const int LastYears = 2;

    // The freezes of a family in the order they came, read off its live rule at each instant its register rows were
    // written, in that order: each instant its live rule names a rule other than the one standing just before it, none
    // standing before the first. An instant no live rule stands at is no freeze, and a rule standing again after one is.
    public static IReadOnlyList<SweepFreeze> Freezes(IEnumerable<(DateTimeOffset At, string? Live)> lives)
    {
        var freezes = new List<SweepFreeze>();
        string? before = null;

        foreach (var (at, live) in lives.OrderBy(one => one.At))
        {
            if (live is not null && !string.Equals(live, before, StringComparison.Ordinal))
            {
                freezes.Add(new SweepFreeze(live, at));
            }

            before = live;
        }

        return freezes;
    }

    // Each design's newest answer, by the instant it was recorded and then by its run, a family sweeping one design
    // reading one.
    public static IReadOnlyList<RecordedAnswer> Newest(IReadOnlyList<RecordedAnswer> answers) =>
    [
        .. answers
            .GroupBy(answer => answer.Design ?? string.Empty, StringComparer.Ordinal)
            .Select(design => design.OrderByDescending(answer => answer.RecordedAt).ThenByDescending(answer => answer.Run, StringComparer.Ordinal).First()),
    ];

    // The freezes at or after the newest answer, the first of them its freeze: a freeze at the answer's own instant
    // counts as made on it.
    public static IReadOnlyList<SweepFreeze> After(IReadOnlyList<RecordedAnswer> answers, IReadOnlyList<SweepFreeze> freezes)
    {
        var newest = Newest(answers);

        if (newest.Count == 0)
        {
            return [];
        }

        var at = newest.Max(answer => answer.RecordedAt);

        return [.. freezes.Where(freeze => freeze.At >= at).OrderBy(freeze => freeze.At)];
    }

    public static bool Drawn(IReadOnlyList<RecordedAnswer> answers, IReadOnlyList<SweepFreeze> freezes, string? live)
    {
        var newest = Newest(answers);

        if (newest.Count == 0 || !newest.All(answer => string.Equals(answer.Answer, SweepAnswer.NonePassedWord, StringComparison.Ordinal)))
        {
            return false;
        }

        var after = After(answers, freezes);

        return live is null || after.Count == 0 || (after.Count == 1 && string.Equals(after[0].Rule, live, StringComparison.Ordinal));
    }

    // The freeze a drawn line is kept at: its freeze, where it made the live rule standing now and none came after it,
    // none where the line is drawn for an answer recorded after the freeze or on a card no live rule reads.
    public static SweepFreeze? KeptAt(IReadOnlyList<RecordedAnswer> answers, IReadOnlyList<SweepFreeze> freezes, string? live) =>
        live is not null && Drawn(answers, freezes, live) && After(answers, freezes) is [var kept] ? kept : null;

    // The freeze that made the live rule standing now: the family's last, where it names that rule.
    public static SweepFreeze? LiveFreeze(IReadOnlyList<SweepFreeze> freezes, string? live) =>
        live is not null && freezes.Count > 0 && string.Equals(freezes[^1].Rule, live, StringComparison.Ordinal) ? freezes[^1] : null;

    // The answer a freeze followed: the newest recorded at or before its instant, of the design given where the family
    // sweeps more than one.
    public static RecordedAnswer? Followed(IReadOnlyList<RecordedAnswer> answers, SweepFreeze freeze, string? design = null) =>
        answers
            .Where(answer => answer.RecordedAt <= freeze.At && (design is null || string.Equals(answer.Design, design, StringComparison.Ordinal)))
            .OrderByDescending(answer => answer.RecordedAt)
            .ThenByDescending(answer => answer.Run, StringComparer.Ordinal)
            .FirstOrDefault();

    // The last years a run's figures store, oldest first, each with its edge.
    public static IReadOnlyList<(int Year, double? Edge)> Last(AnswerFigures figures) =>
        [.. figures.YearEdges.OrderBy(year => year.Key).TakeLast(LastYears).Select(year => (year.Key, year.Value))];

    // The last years a run's figures store in which its setting's edge fell below nothing, none where it lost in neither
    // or a year holds no edge.
    public static IReadOnlyList<int> LostIn(AnswerFigures figures) =>
        [.. Last(figures).Where(year => year.Edge is < 0).Select(year => year.Year)];

    // How many of the years a run's figures store its setting's edge stood above nothing in.
    public static int YearsAbove(AnswerFigures figures) => figures.YearEdges.Count(year => year.Value is > 0);

    // The words a kept line adds for the years the setting frozen at it lost: both years, one year, or none.
    public static string LostLine(IReadOnlyList<int> lost) =>
        lost.Count == 0 ? string.Empty : LostWords + string.Join(" and ", lost.Select(year => year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
