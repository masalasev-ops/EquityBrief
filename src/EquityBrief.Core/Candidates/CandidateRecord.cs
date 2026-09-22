using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Candidates;

// One setup as a candidate's record reads it: what it did, the bar its own plan
// set, the bar the calibration set, and what the trade came to.
//
// The calibrated bar is nullable and the record counts no setup without one: a
// setup whose bar nothing computed is a setup nothing can be compared against,
// and reading a missing bar as the planned one would be scoring some setups
// against one yardstick and some against another.
public readonly record struct CandidateSetup(
    DateOnly Session,
    string Outcome,
    double? Null,
    double? NullAtSensitivity,
    double? BreakEven,
    double? ReturnPct,
    double? PlannedRisk,
    bool OnEarnings);

// What one look read.
public sealed record LookRead(
    int Blocks,
    int Setups,
    int Wins,
    double Share,
    double NullShare,
    double Statistic,
    double PValue,
    double PoissonBinomial,
    double Level,
    double Spends,
    bool Crossed,
    bool Futile,
    double? SmallestExcess);

// A candidate's record: what its setups have come to, what its looks have read,
// and what the next look waits for.
public sealed record Measured(
    int Blocks,
    int Floor,
    int Setups,
    int Wins,
    int SameSession,
    int EarningsStopOuts,
    int NotYetInABlock,
    double? Share,
    double? NullShare,
    double? NullShareAtSensitivity,
    double? Excess,
    double? PValue,
    double? PoissonBinomial,
    double? DesignEffect,
    double? RealizedLoss,
    double? RealizedLossHigh,
    double? RealizedBreakEven,
    double? PlannedBreakEven,
    IReadOnlyList<LookRead> Looks,
    int LooksRemaining,
    int? NextLookAt,
    string Verdict,
    string Withheld);

// What a registered candidate's setups come to, read at the looks it was
// registered with.
//
// The arithmetic sits here and the page draws it, as every other record in this
// corpus does: a rendering layer that computes is a second implementation of one
// rule. Nothing here decides whether a record is shown. That is the floor's job,
// and the floor is eight non-empty blocks.
// see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
// see: A screen reads and renders, and computes nothing
public static class CandidateRecord
{
    // The words a record carries in place of a verdict, and the words of one.
    public const string BelowTheBlockFloor = "blocks";

    public const string Shown = "none";

    public const string NotCrossed = "the last look did not cross its boundary";

    public const string Crossed = "the boundary was crossed: a promotion is due";

    public const string Futile = "the futility guideline is met: a retirement is due";

    public const string LastLookPassed = "the last look passed without crossing: a retirement is due";

    public const string NoLookYet = "no look has been read";

    // The looks the futility guideline is read at, which are the first two and
    // never the last: a candidate at its last look is retired by reaching it.
    // see: A candidate is retired by a futility guideline at its first two looks or by reaching its last look
    public const int FutilityLooks = 2;

    public static Measured For(IReadOnlyList<CandidateSetup> setups, DateOnly? first, DateOnly night, double level)
    {
        var scored = setups
            .Where(setup => setup.Outcome is ForwardReturnSeries.Win or ForwardReturnSeries.Loss && setup.Null is not null)
            .OrderBy(setup => setup.Session)
            .ToArray();

        var opened = first ?? (scored.Length == 0 ? night : scored[0].Session);

        // Whole blocks and in order. A block counts once every session it holds
        // has had its outcome window, and a block holding no scored setup is not
        // one of the blocks the test is over.
        var blocks = scored
            .Where(setup => Blocks.Complete(opened, Blocks.Of(opened, setup.Session), night))
            .GroupBy(setup => Blocks.Of(opened, setup.Session))
            .OrderBy(group => group.Key)
            .Select(group => group.ToArray())
            .ToArray();

        var counted = blocks.SelectMany(block => block).ToArray();
        var waiting = scored.Length - counted.Length;
        var sums = blocks.Select(Excess).ToArray();
        var quiet = new List<LookRead>();

        var measured = new Measured(
            blocks.Length,
            Blocks.Floor,
            counted.Length,
            counted.Count(setup => setup.Outcome == ForwardReturnSeries.Win),
            counted.Count(IsSameSession),
            counted.Count(setup => setup.OnEarnings && setup.Outcome == ForwardReturnSeries.Loss),
            waiting,
            Share(counted),
            NullShare(counted, setup => setup.Null),
            NullShare(counted, setup => setup.NullAtSensitivity),
            Excess(counted) is var excess && counted.Length > 0 ? excess / counted.Length * 100 : null,
            // The running figure is read over the blocks the last look reads and
            // never over more: the maximum is not extended, and a figure over
            // seventeen blocks would be a look nobody registered wearing the
            // clothes of a monitoring figure.
            blocks.Length >= 2 ? SignFlip.PValue(Take(sums, Math.Min(blocks.Length, Looks.Maximum))) : null,
            PoissonBinomialFor(counted),
            DesignEffect(sums, counted),
            RealizedLoss(counted, high: false),
            RealizedLoss(counted, high: true),
            RealizedBreakEven(counted),
            PlannedBreakEven(counted),
            quiet,
            Looks.At.Count,
            Looks.At[0],
            NoLookYet,
            blocks.Length < Blocks.Floor ? BelowTheBlockFloor : Shown);

        if (blocks.Length < Blocks.Floor)
        {
            return measured;
        }

        var reads = new List<LookRead>();
        var crossedAt = (int?)null;

        for (var look = 0; look < Looks.Taken(Math.Min(blocks.Length, Looks.Maximum)); look++)
        {
            var over = blocks.Take(Looks.At[look]).ToArray();
            var inside = over.SelectMany(block => block).ToArray();
            var prefix = Take(sums, Looks.At[look]);
            var spends = Looks.Spent(level, Looks.Fraction(look)) - (look == 0 ? 0 : Looks.Spent(level, Looks.Fraction(look - 1)));
            var reached = Looks.CrossedAt(prefix, level);

            crossedAt ??= reached == look ? look : null;

            var share = Share(inside) ?? 0;
            var nulls = NullShare(inside, setup => setup.Null) ?? 0;

            reads.Add(new LookRead(
                Looks.At[look],
                inside.Length,
                inside.Count(setup => setup.Outcome == ForwardReturnSeries.Win),
                share,
                nulls,
                SignFlip.Statistic(prefix),
                SignFlip.PValue(prefix),
                PoissonBinomialFor(inside) ?? 1,
                level,
                spends,
                reached == look,
                look < FutilityLooks && share < nulls,
                // The design effect is read at no less than one. Blocks that happen to scatter
                // less than independent setups would give a figure below it, and stating the
                // smaller excess that implies would be claiming the block structure bought power
                // rather than cost it, off an estimate made from eight numbers.
                Looks.SmallestExcess(
                    spends,
                    Looks.At[look],
                    (double)inside.Length / Looks.At[look],
                    nulls / 100,
                    Math.Max(DesignEffect(prefix, inside) ?? 1, 1))));
        }

        var last = reads[^1];
        var remaining = Looks.At.Count - reads.Count;

        return measured with
        {
            Looks = reads,
            LooksRemaining = remaining,
            NextLookAt = remaining > 0 ? Looks.At[reads.Count] : null,
            Verdict = crossedAt is not null
                ? Crossed
                : last.Futile
                    ? Futile
                    : remaining == 0
                        ? LastLookPassed
                        : NotCrossed,
        };
    }

    // A block's excess: the wins inside it less the bars their own plans and the
    // calibration set for them. It is the quantity whose sign the test flips.
    public static double Excess(IReadOnlyList<CandidateSetup> setups) =>
        setups.Sum(setup => (setup.Outcome == ForwardReturnSeries.Win ? 1 : 0) - setup.Null!.Value);

    // A setup entered and stopped on one session, which is a loss whose fill the
    // store does not place and so carries no bar of its own. The record counts it
    // as the loss it is, against the bar the calibration took from the worst fill
    // the zone offered.
    // see: A candidate counts a setup entered and stopped in one session as a loss
    public static bool IsSameSession(CandidateSetup setup) =>
        setup.Outcome == ForwardReturnSeries.Loss && setup.BreakEven is null;

    static double[] Take(IReadOnlyList<double> sums, int blocks) => [.. sums.Take(blocks)];

    static double? Share(IReadOnlyList<CandidateSetup> setups) =>
        setups.Count == 0 ? null : (double)setups.Count(setup => setup.Outcome == ForwardReturnSeries.Win) / setups.Count * 100;

    static double? NullShare(IReadOnlyList<CandidateSetup> setups, Func<CandidateSetup, double?> bar)
    {
        var bars = setups.Select(bar).OfType<double>().ToArray();

        return bars.Length == setups.Count && bars.Length > 0 ? bars.Average() * 100 : null;
    }

    // The tail of the distribution the live family's test reads, over the same
    // setups and the calibrated bars. It stands beside the verdict, labelled, and
    // decides nothing: it assumes the setups are independent, and the blocks exist
    // because they are not.
    static double? PoissonBinomialFor(IReadOnlyList<CandidateSetup> setups) =>
        setups.Count == 0 || setups.Any(setup => setup.Null is null)
            ? null
            : PoissonBinomial.UpperTail(
                [.. setups.Select(setup => setup.Null!.Value)],
                setups.Count(setup => setup.Outcome == ForwardReturnSeries.Win));

    // How much wider the blocks make the record than independent setups would:
    // the scatter the blocks actually show over the scatter the bars imply.
    //
    // Noisy near the floor, where it is estimated from eight numbers, which is
    // why the surface says so beside it.
    static double? DesignEffect(IReadOnlyList<double> sums, IReadOnlyList<CandidateSetup> setups)
    {
        if (sums.Count < 2 || setups.Count == 0)
        {
            return null;
        }

        var independent = setups.Sum(setup => setup.Null!.Value * (1 - setup.Null!.Value));

        if (independent <= 0)
        {
            return null;
        }

        var mean = sums.Average();

        return sums.Count * sums.Sum(sum => (sum - mean) * (sum - mean)) / (sums.Count - 1) / independent;
    }

    // What a loss actually cost, in multiples of what the plan put at risk: the
    // mean, and the ninetieth of them by rank. Reported beside every verdict and
    // tested nowhere, because the test is about how often a plan was right and
    // this is about what being wrong came to.
    static double? RealizedLoss(IReadOnlyList<CandidateSetup> setups, bool high)
    {
        var multiples = setups
            .Where(setup => setup.Outcome == ForwardReturnSeries.Loss && setup.ReturnPct is < 0 && setup.PlannedRisk is > 0)
            .Select(setup => -setup.ReturnPct!.Value / setup.PlannedRisk!.Value)
            .OrderBy(multiple => multiple)
            .ToArray();

        if (multiples.Length == 0)
        {
            return null;
        }

        return high
            ? multiples[Math.Min(multiples.Length - 1, (int)Math.Ceiling(0.9 * multiples.Length) - 1)]
            : multiples.Average();
    }

    // The share the trades would have had to win to come out even, measured from
    // what they made and lost rather than from where the plans put their stops.
    static double? RealizedBreakEven(IReadOnlyList<CandidateSetup> setups)
    {
        var wins = setups.Where(setup => setup.Outcome == ForwardReturnSeries.Win && setup.ReturnPct is > 0).ToArray();
        var losses = setups.Where(setup => setup.Outcome == ForwardReturnSeries.Loss && setup.ReturnPct is < 0).ToArray();

        if (wins.Length == 0 || losses.Length == 0)
        {
            return null;
        }

        var gain = wins.Average(setup => setup.ReturnPct!.Value);
        var loss = -losses.Average(setup => setup.ReturnPct!.Value);

        return gain + loss <= 0 ? null : loss / (gain + loss) * 100;
    }

    static double? PlannedBreakEven(IReadOnlyList<CandidateSetup> setups)
    {
        var bars = setups.Select(setup => setup.BreakEven).OfType<double>().ToArray();

        return bars.Length == 0 ? null : bars.Average();
    }
}
