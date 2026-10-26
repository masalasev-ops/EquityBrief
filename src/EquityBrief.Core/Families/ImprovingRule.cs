using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Families;

// What the Fundamentals family's rule reads: the score a newly read filing must lift a stock to and the score it is held
// at or above, each stated in nine and read as a share of the checks that applied, and the most sessions a trade is held.
public sealed record ImprovingSettings(int BuyInNine, int HoldInNine, int Sessions)
{
    public string Words => string.Create(
        CultureInfo.InvariantCulture,
        $"a newly read filing lifting its score to at least {BuyInNine} in 9 of the checks that apply and above its previous filing's, with its sales growth over a year faster than the quarter before; bought at the next open, held while its score stays at {HoldInNine} in 9 or better, and sold at the close of the night a new filing drops it below that, at the close of its {Sessions}th session or at its last close as a member");
}

// Whether a member whose newest filing was read on the night is bought, and why not where it is not.
public sealed record ImprovingAnswer(bool Bought, string? Reason);

// The Fundamentals family: a company whose newest filing, read for the first time on the night, lifts its scorecard to
// eight in nine or better and above the score its previous filing held, while its sales growth speeds up, bought at the
// next open and held while its score stays at seven in nine or better, with no chart setup and no pullback setting; and,
// beside it, a company whose results release since its last stored filing reads better results and an outlook raised or
// kept, flagged and bought on nothing. The score is the core's one scorecard over the filings as first filed, after
// Piotroski (2000).
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
public static class ImprovingRule
{
    public const string Name = "improving";

    public const string Label = "Fundamentals";

    public const string Description = "Finds companies whose business is strong or getting stronger, read from their filings with the SEC and their results releases.";

    // The line its tile states what it looks for in.
    public const string Looks = "A company whose business is strong or getting stronger, read from its filings and results release.";

    // The S&P 500's code in the store, the first of the three indices the family reads.
    public const string LargeIndex = "GSPC";

    public static ImprovingSettings Starting { get; } = new(8, 7, 126);

    // The release's words a flag reads: results better, and an outlook raised or kept.
    public const string Better = "better";

    public static IReadOnlyList<string> FlaggedOutlooks { get; } = ["raised", "kept"];

    // What became of a member on the night, as the step stores it and the card draws it: bought at the next open, its
    // newly read filing raising its score short of the bar, flagged from its release, or already held.
    public const string Bought = "bought";
    public const string Improved = "improved";
    public const string Flagged = "flagged";
    public const string Held = "held";

    // Why a trade ended, as the step stores it and the card draws it, and the word for one still open.
    public const string Open = "open";
    public const string ScoreFell = "score fell";
    public const string TimeLimit = "time limit";
    public const string LeftTheIndex = "left the index";

    // Why a member whose newest filing was read on the night is not bought, in the rule's order.
    public const string NoWord = "too few checks read for a score";
    public const string NotHigher = "no higher than its previous filing's score";
    public const string UnderTheBar = "a score under the bar";
    public const string GrowthNotRead = "its sales growth not read";
    public const string NotFaster = "its sales growth not faster than the quarter before";

    // A flag: a release accepted after the newest filing the scorecard reads, reading results better and an outlook
    // raised or kept. A flag buys nothing.
    public static bool Flags(ReleaseAnswer answer) =>
        answer.Results == Better && FlaggedOutlooks.Contains(answer.Outlook, StringComparer.Ordinal);

    // Whether a release was accepted after a filing: its day in New York later than the filing's day.
    public static bool After(DateOnly releasedOn, DateOnly? filedOn) => filedOn is not { } filed || releasedOn > filed;

    // The release a filing confirmed: one accepted after its quarter ended and on or before the filing's day, none
    // otherwise.
    public static DateOnly? Confirmed(DateOnly? releasedOn, DateOnly? quarter, DateOnly? filedOn) =>
        releasedOn is { } released && quarter is { } end && filedOn is { } filed && released > end && released <= filed ? released : null;

    // A member whose newest filing was read on the night: bought where its score clears the bar, stands above its previous
    // filing's and its sales growth over a year is faster than the quarter before's; its reason otherwise.
    public static ImprovingAnswer Answer(int passed, int applied, int passedBefore, int appliedBefore, double? growthChange, ImprovingSettings settings) =>
        applied < FundamentalScorecard.FewestRead ? new(false, NoWord)
        : !(Share(passed, applied) > Share(passedBefore, appliedBefore)) ? new(false, NotHigher)
        : !FundamentalScorecard.Clears(passed, applied, settings.BuyInNine) ? new(false, UnderTheBar)
        : growthChange is not { } change ? new(false, GrowthNotRead)
        : !(change > 0) ? new(false, NotFaster)
        : new(true, null);

    // Whether a trade is held: its score at the hold floor or better, read as a share of the checks that applied.
    public static bool Holds(int passed, int applied, ImprovingSettings settings) =>
        FundamentalScorecard.Clears(passed, applied, settings.HoldInNine);

    public static double Share(int passed, int applied) => applied > 0 ? 1.0 * passed / applied : 0;

    // How far a filing lifted the score, as shares of the checks that applied.
    public static double Rise(int passed, int applied, int passedBefore, int appliedBefore) =>
        Share(passed, applied) - Share(passedBefore, appliedBefore);

    // The order the improvers are drawn in: the score's rise, the larger first and none last; then how much faster its
    // sales grew than the quarter before, the larger first and none last; then the ticker.
    public static IReadOnlyList<T> Ranked<T>(IEnumerable<T> rows, Func<T, (double? Rise, double? Faster, string Ticker)> of) =>
        [.. rows
            .OrderBy(row => of(row).Rise is null)
            .ThenByDescending(row => of(row).Rise ?? 0)
            .ThenBy(row => of(row).Faster is null)
            .ThenByDescending(row => of(row).Faster ?? 0)
            .ThenBy(row => of(row).Ticker, StringComparer.Ordinal)];

    // The day the newest filing the scorecard reads on a session was first filed: the earliest filing before the session
    // stating net income for a span ending on its newest quarter. None where it reads no quarter.
    public static DateOnly? FiledOn(IReadOnlyList<FiledFactRow> facts, DateOnly? quarter, DateOnly session) =>
        quarter is not { } end
            ? null
            : facts
                .Where(fact => fact.End == end && fact.Filed < session && fact.Start < fact.End && FiledFacts.NetIncome.Contains(fact.Concept, StringComparer.Ordinal))
                .Select(fact => (DateOnly?)fact.Filed)
                .Min();

    // Whether a filing is read for the first time on a night: filed on or after the session the family last read, which a
    // filing made that day was not read on, and before the night's.
    public static bool FirstRead(DateOnly filed, DateOnly lastRead, DateOnly night) => filed >= lastRead && filed < night;

    // The session before a night on the exchange's calendar, the family's first night reading the filings since it.
    public static DateOnly SessionBefore(DateOnly night)
    {
        var day = night.AddDays(-1);

        while (day >= ExchangeClosures.CoveredFrom && !ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(-1);
        }

        return day;
    }
}
