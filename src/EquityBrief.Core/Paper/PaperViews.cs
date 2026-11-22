using System.Globalization;
using EquityBrief.Core.Cards;

namespace EquityBrief.Core.Paper;

// One revision of a rule as the pages draw it: its number, the first session it ran, the last where a later revision followed
// it, none while it runs, and what it changed from the revision before in the words the rule's settings are drawn in, none
// for the first.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public sealed record RevisionView(int Number, DateOnly From, DateOnly? Until, string? Changed)
{
    // What a revision changed where its settings read the same and a change in its code changed what its trades do.
    public const string CodeOnly = "The same settings, with a change in its code that changed what its trades do";

    public bool Running => Until is null;

    // The registration that started it, which its plan is read from.
    public long Registration { get; init; }

    public string Name => Named(Number);

    public static string Named(int number) => "Revision " + number.ToString(CultureInfo.InvariantCulture);

    // "Revision 9, running since 16 Nov 2026", or "Revision 8, ran 30 Oct to 13 Nov 2026" for one no longer running.
    public string Tag => Until is { } until
        ? Name + ", ran " + Ran(From, until)
        : Name + ", running since " + Day(From);

    // The sessions it ran, the year once where both fall in it.
    public static string Ran(DateOnly from, DateOnly until) =>
        from.Year == until.Year
            ? from.ToString("d MMM", CultureInfo.InvariantCulture) + " to " + Day(until)
            : Day(from) + " to " + Day(until);

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

// One setting a variant states differently from its running revision: the chip's key, its short words and the plain sentence
// a page names it by.
public sealed record ChangeLine(string Key, string Words, string Sentence);

// One test year of a rule in its index's newest per-rule test: its units and total, and the side it was read against's.
public sealed record PaperYearView(int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// One paper account as paper trading and the trade log draw it: its key in the link, its family, its standing (live,
// provisional, variant or the operator's own), its name and its rule's words, its test years verdict with what it was
// read against, its unit and its test years, its walk where the account settings are set, its trades and those ended,
// its record since registration's label until the count screen's minimum is met, the day it starts, its adjusted p in
// that test, its record's count in one line, the words naming the version it counts, and how many trades it leaves out
// as listed before that version. Paper trading's page draws besides its short name and what it changes in words, its
// trades against its family's others, the years its test counted it better than what it was read against, each test year
// as a box, where its adjusted p stands on the scale of luck, its progress toward the count a verdict reads, and the
// trades its walk holds open and has closed, each worked by the core.
public sealed record PaperAccountView(
    string Key,
    string Family,
    string Standing,
    string Name,
    string Words,
    string Verdict,
    string? Against,
    string Unit,
    IReadOnlyList<PaperYearView> Years,
    PaperWalk? Walk,
    int Trades,
    int Ended,
    string? TooFew,
    DateOnly? Since,
    double? Adjusted = null,
    string? CountLine = null,
    string? Version = null,
    int Earlier = 0,
    string? Short = null,
    string? Changes = null,
    PaperOverlap? Overlap = null,
    int? Better = null,
    int? Counted = null,
    IReadOnlyList<PaperYear>? Boxes = null,
    PaperLuck? Luck = null,
    PaperProgress? Progress = null,
    int Open = 0,
    int Closed = 0,
    string? Note = null)
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    public const string You = "you";

    public bool Head => Standing is Live or Provisional;

    // The local model's cutoff's year a rule reading a release reading is judged from, none for any other.
    public int? JudgedFrom { get; init; }

    // The rule as the newest per-rule test stored it, which a verdict across the three indices names; none where no test
    // read it.
    public string? Tested { get; init; }

    // The first session its version's registration counts from, the day it starts, none where no registration starts the
    // account.
    public DateOnly? VersionFrom { get; init; }

    // A version's reach against its live rule, none for any other account.
    public VersionReachKind? Reach { get; init; }

    // The revisions of its rule up to the one it stands in, the last that one; none for an account no registration starts.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public IReadOnlyList<RevisionView> Revisions { get; init; } = [];

    // The revision it stands in, none for an account no registration starts.
    public RevisionView? Revision => Revisions.Count > 0 ? Revisions[^1] : null;

    // A variant's settings that differ from its running revision, each a plain sentence; none for any other account.
    public IReadOnlyList<ChangeLine> Differences { get; init; } = [];
}
