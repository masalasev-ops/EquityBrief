using EquityBrief.Core.Families;

namespace EquityBrief.Core.Loop;

// The monthly per-rule test over the long history: each family judged from its own first test year, the price families'
// from 2008, a year with a full one of learning behind it after 2005's first, and the drift's and the filing-based
// families' from the years 23.5 measured; and the time it may take, capped, its sampled sign vectors cut to fit and never
// below the fewest that tell a result from the bar within the stated margin.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public static class LongTest
{
    public const string Standing = "standing";

    public const string Long = "long";

    // The price families' first test year, and the first year a variant reading HYG may be judged from, HYG starting in
    // 2007.
    public const int PriceFirstTestYear = 2008;

    // The drift's and the filing-based families' first test years on each index as 23.5 measured them, three learning
    // years after the first year 90 in every 100 members read the family's score, the drift's the first its surprises
    // cover 80 in every 100 of the since-gone members.
    public static IReadOnlyDictionary<(string Family, string Index), int> Measured { get; } = new Dictionary<(string, string), int>
    {
        [(ImprovingRule.Name, "GSPC")] = 2016,
        [(ImprovingRule.Name, "MID")] = 2016,
        [(ImprovingRule.Name, "SML")] = 2017,
        [(FundamentalsRule.Name, "GSPC")] = 2016,
        [(FundamentalsRule.Name, "MID")] = 2016,
        [(FundamentalsRule.Name, "SML")] = 2017,
        [(QualityRule.Name, "GSPC")] = 2016,
        [(QualityRule.Name, "MID")] = 2016,
        [(QualityRule.Name, "SML")] = 2017,
        [(HeavyweightRule.Name, "GSPC")] = 2023,
        [(HeavyweightRule.Name, "MID")] = 2015,
        [(HeavyweightRule.Name, "SML")] = 2016,
        [(DriftRule.Name, "GSPC")] = 2016,
        [(DriftRule.Name, "MID")] = 2016,
        [(DriftRule.Name, "SML")] = 2016,
    };

    // A family's first test year on an index over the long history: its measured year, or the price families' first.
    public static int FirstTestYear(string family, string index) =>
        Measured.TryGetValue((family, index), out var year) ? year : PriceFirstTestYear;

    // The most the long test may run in a month.
    public static TimeSpan Cap { get; } = TimeSpan.FromHours(3);

    // The margin the fewest sign vectors read a p-value at the bar within: two standard errors of the share at the bar, a
    // relative 15 in every 100 of it.
    public const double Margin = 0.15;

    public const double StandardErrors = 2;

    // The fewest sign vectors the cut may leave: the count whose two standard errors of a share at the bar come to the
    // margin of it, z squared times the bar's complement over the margin squared times the bar, at least 20,000.
    public static long FewestDraws { get; } = Math.Max(
        LeastDraws,
        (long)Math.Ceiling(StandardErrors * StandardErrors * (1 - LoopGate.Bar) / (Margin * Margin * LoopGate.Bar)));

    public const long LeastDraws = 20_000;

    // The sign vectors the gate samples past its enumerated blocks when nothing cuts them.
    public static long FullDraws => 1L << LoopGate.Enumerated;

    // The sign vectors an index's judgement reads: every one while the step has used under half its cap, and the fewest
    // once it has used half, so the indices after it fit what is left; a cap handed in stands for the stated one.
    public static long DrawsAt(TimeSpan elapsed, TimeSpan? cap = null) => elapsed < (cap ?? Cap) / 2 ? FullDraws : FewestDraws;

    // Whether the step starts another family: while its time stands under its cap. From the 24.19 correction of 2026-11-27
    // it is read before each family of each index, and each line of a family not reached is written by name with why.
    // see: The long test reads its cap before each family and keeps every verdict judged before it, each line it did not reach named and walked first the month after
    public static bool GoesOn(TimeSpan elapsed, TimeSpan? cap = null) => elapsed < (cap ?? Cap);

    // The indices in the order the long test takes them: the smallest first, so a cut falls on the fewest lines.
    public static IReadOnlyList<string> Indices { get; } = ["SML", "MID", "GSPC"];

    // The order the long test takes the indices in: each holding a line the month before did not reach first, then the
    // rest, each in the smallest-first order.
    public static IReadOnlyList<string> IndexOrder(IReadOnlyCollection<(string Index, string Family)> notReached) =>
        [.. Indices.OrderBy(index => notReached.Any(one => one.Index == index) ? 0 : 1)];

    // The order the long test walks an index's families in: each the month before did not reach first, then the two books,
    // Quality at a fair price and the Fundamentals family, then each family holding a registered variant, then the rest,
    // each group in the page's order; the walks written nowhere ride with their family, and the one walked after every
    // family, the graded exposure reversed, is walked only while the cap stands.
    public static IReadOnlyList<string> FamilyOrder(IReadOnlyList<string> families, IReadOnlyCollection<string> notReached, Func<string, bool> holdsVariant) =>
    [
        .. families
            .Select((family, at) => (Family: family, At: at))
            .OrderBy(one => notReached.Contains(one.Family) ? 0 : one.Family is QualityRule.Name or ImprovingRule.Name ? 1 : holdsVariant(one.Family) ? 2 : 3)
            .ThenBy(one => one.At)
            .Select(one => one.Family),
    ];
}
