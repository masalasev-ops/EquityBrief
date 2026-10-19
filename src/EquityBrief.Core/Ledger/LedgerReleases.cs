using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Ledger;

// One results release as the ledger reads it: the instant the SEC accepted it, and the four words the local model
// answered for it under the reading version the ledger reads, none where that reading was refused or is not held.
public sealed record LedgerRelease(DateTimeOffset Accepted, ReleaseAnswer? Answer);

// A proposal's or a rule's test years on each side of the model's cutoff: the years, the units and the proposal's and the
// rule's totals, before the cutoff's year and from it.
public sealed record CutoffSides(
    IReadOnlyList<int> BeforeYears,
    int BeforeUnits,
    double BeforeProposed,
    double BeforeCurrent,
    IReadOnlyList<int> AfterYears,
    int AfterUnits,
    double AfterProposed,
    double AfterCurrent);

// From 20.8 the ledger's four release readings: the local model's answers on the newest results release the night of a
// session may read that the SEC accepted within the window's days before the session, each word the reading the
// questions' decision turns it into. All four are none where no release in the window is readable, and where the newest
// one's reading was refused or is not held, since an older release would answer for another quarter; a word saying the
// release states nothing is none and never nought.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public static class LedgerReleases
{
    // The days before a session the release read on it may have been accepted on: a quarter's results release and not
    // the quarter's before.
    public const int WindowDays = 95;

    // The four readings' columns, in the catalogue's order and the questions'.
    public static IReadOnlyList<string> Columns { get; } = ["release_results", "release_outlook", "release_tone", "release_demand"];

    // The release read on a session: the newest of a filer's that the night of the session may read, accepted on a day,
    // in UTC, no more than the window's days before the session.
    public static LedgerRelease? On(IReadOnlyList<LedgerRelease> releases, DateOnly session)
    {
        var from = session.AddDays(-WindowDays);
        LedgerRelease? newest = null;

        foreach (var release in releases)
        {
            if (ReleaseAvailability.ReadableOn(release.Accepted, session)
                && DateOnly.FromDateTime(release.Accepted.UtcDateTime) >= from
                && (newest is null || release.Accepted > newest.Accepted))
            {
                newest = release;
            }
        }

        return newest;
    }

    // The words a rule's hooks state for a tree model, which reads every reading of the catalogue it was fitted over.
    public const string TreesWords = "ordered by the learned trees";

    // Whether a change's words or a rule's read a release reading: a condition or a weight naming one of the four
    // columns, a tree model, fitted over the whole catalogue, or the drift's tone condition.
    public static bool Reads(string? words) =>
        words is not null
        && (Columns.Any(column => words.Contains(column, StringComparison.Ordinal))
            || words.Contains(TreesWords, StringComparison.Ordinal)
            || words.Contains(ReleaseTone.Words, StringComparison.Ordinal));

    // A proposal's or a rule's test years split at the model's cutoff, the years before the cutoff's own year on one side
    // and those from it on the other, each side's units and its two totals summed.
    public static CutoffSides Split(IEnumerable<(int Year, int Units, double Proposed, double Current)> years, DateOnly cutoff)
    {
        var (beforeUnits, beforeProposed, beforeCurrent, afterUnits, afterProposed, afterCurrent) = (0, 0.0, 0.0, 0, 0.0, 0.0);
        var (beforeYears, afterYears) = (new List<int>(), new List<int>());

        foreach (var (year, units, proposed, current) in years)
        {
            if (year < cutoff.Year)
            {
                (beforeUnits, beforeProposed, beforeCurrent) = (beforeUnits + units, beforeProposed + proposed, beforeCurrent + current);
                beforeYears.Add(year);
            }
            else
            {
                (afterUnits, afterProposed, afterCurrent) = (afterUnits + units, afterProposed + proposed, afterCurrent + current);
                afterYears.Add(year);
            }
        }

        return new CutoffSides(beforeYears, beforeUnits, beforeProposed, beforeCurrent, afterYears, afterUnits, afterProposed, afterCurrent);
    }

    // The four readings of a release in the columns' order, each none where no answer is held.
    public static IReadOnlyList<double?> Readings(LedgerRelease? release) =>
        release?.Answer is { } answer
            ? [.. ReleaseQuestions.All.Select(question => Reading(answer, question.Key))]
            : [null, null, null, null];

    // A word's reading, the whole number its question gives it, held as the catalogue holds every reading.
    static double? Reading(ReleaseAnswer answer, string key) => answer.Reading(key);
}
