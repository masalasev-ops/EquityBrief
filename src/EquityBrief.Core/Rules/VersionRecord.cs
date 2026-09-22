using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Rules;

// What one version's difference from the live rule has come to, read at the same
// looks a candidate's record is read at.
public sealed record VersionMeasured(
    string Version,
    int Blocks,
    int Floor,
    int LiveSetups,
    int VersionSetups,
    double? Excess,
    double? Statistic,
    double? PValue,
    int? CrossedAt,
    string Verdict,
    IReadOnlyList<double> Differences);

// A version judged against the live rule, on the difference between them.
//
// The same test a candidate is judged by, over the same blocks, and the quantity
// whose signs are flipped is the difference: the version's excess in a block less
// the live rule's. A version is not a new condition to be scored on its own, it
// is a change to one that is already running, and the question it has to answer
// is whether the change did anything. Scoring a version alone would put a
// verdict on the rule rather than on the change, and both versions of a rule
// that works would cross.
//
// The setups on either side are not paired name by name, because a version
// changes which names hold a plan at all. They are paired by block, which is the
// unit the test is over.
// see: A trend version is judged by the candidates' test on its difference from the live rule
// see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
public static class VersionRecord
{
    // The words a version's record carries in place of a verdict, and the words
    // of one. A version that crossed is not promoted by crossing: the operator
    // rules, and the ruling closes the live window and opens the version's as the
    // rule the night runs.
    public const string BelowTheFloor = "not yet, under the floor of blocks";

    public const string NotCrossed = "no difference from the live rule at its looks so far";

    public const string Crossed = "a difference from the live rule at the level its look spends";

    // How far ahead the wider version has to be for it to be kept over the
    // narrower one, in points of win share.
    //
    // Fixed before anything was scored and settled by no reading, which is what
    // keeps it from being a threshold chosen on its own outcomes. The narrower
    // version is kept where the two are within it, because the evidence behind
    // the narrower one is the evidence a study supports and the wider one adds
    // names on a judgement.
    // see: No parameter is settled from the outcomes of the candidate or version it belongs to
    public const double MarginInPoints = 5;

    // A version's difference from the live rule, one number per block.
    //
    // A block counts where either side listed a setup in it and the block has
    // had its whole outcome window, so a version that removed every setup in a
    // block still has a block there: removing them is the difference being
    // measured, and dropping the block would read the change as no change.
    public static IReadOnlyList<double> Differences(
        IReadOnlyList<CandidateSetup> live,
        IReadOnlyList<CandidateSetup> version,
        DateOnly opened,
        DateOnly night)
    {
        var mine = ByBlock(version, opened, night);
        var theirs = ByBlock(live, opened, night);

        return
        [
            .. mine.Keys
                .Concat(theirs.Keys)
                .Distinct()
                .OrderBy(block => block)
                .Select(block => mine.GetValueOrDefault(block) - theirs.GetValueOrDefault(block)),
        ];
    }

    static IReadOnlyDictionary<int, double> ByBlock(
        IReadOnlyList<CandidateSetup> setups,
        DateOnly opened,
        DateOnly night) =>
        setups
            .Where(setup => setup.Outcome is ForwardReturnSeries.Win or ForwardReturnSeries.Loss && setup.Null is not null)
            .Where(setup => Blocks.Complete(opened, Blocks.Of(opened, setup.Session), night))
            .GroupBy(setup => Blocks.Of(opened, setup.Session))
            .ToDictionary(group => group.Key, group => CandidateRecord.Excess([.. group]));

    public static VersionMeasured For(
        string version,
        IReadOnlyList<CandidateSetup> live,
        IReadOnlyList<CandidateSetup> versionSetups,
        DateOnly opened,
        DateOnly night,
        double level)
    {
        var differences = Differences(live, versionSetups, opened, night);
        var counted = Counted(versionSetups, opened, night);
        var against = Counted(live, opened, night);

        // The look the blocks reach, and no further: a difference read over more
        // blocks than the last look holds is a look nobody opened the window at.
        var read = differences.Count >= Blocks.Floor
            ? [.. differences.Take(Math.Min(differences.Count, Looks.Maximum))]
            : Array.Empty<double>();

        var taken = Looks.Taken(read.Length) is var looks && looks > 0 ? Looks.At[looks - 1] : 0;

        var statistic = taken >= Blocks.Floor ? SignFlip.Statistic([.. read.Take(taken)]) : (double?)null;
        var pValue = taken >= Blocks.Floor ? SignFlip.PValue([.. read.Take(taken)]) : (double?)null;
        var crossed = taken >= Blocks.Floor ? Looks.CrossedAt([.. read.Take(taken)], level) : null;

        return new VersionMeasured(
            version,
            differences.Count,
            Blocks.Floor,
            against.Length,
            counted.Length,
            ExcessInPoints(counted) - ExcessInPoints(against),
            statistic,
            pValue,
            crossed,
            differences.Count < Blocks.Floor ? BelowTheFloor : crossed is not null ? Crossed : NotCrossed,
            differences);
    }

    // A version's excess over the bars its own plans were judged against, in
    // points of win share, which is what the margin between two versions is in.
    public static double? ExcessInPoints(IReadOnlyList<CandidateSetup> counted) =>
        counted.Count == 0 ? null : CandidateRecord.Excess(counted) / counted.Count * 100;

    static CandidateSetup[] Counted(IReadOnlyList<CandidateSetup> setups, DateOnly opened, DateOnly night) =>
    [
        .. setups
            .Where(setup => setup.Outcome is ForwardReturnSeries.Win or ForwardReturnSeries.Loss && setup.Null is not null)
            .Where(setup => Blocks.Complete(opened, Blocks.Of(opened, setup.Session), night)),
    ];

    // Which of two versions of one rule is kept where both crossed: the narrower
    // one, unless the wider is ahead of it by the margin. Null where neither
    // crossed, and the one that crossed where only one did.
    //
    // The narrower version is named second because it is the one kept on a tie,
    // and a rule whose tie-break depends on the order two arguments were passed
    // in is a rule nobody can read off its own call.
    // see: The trend rule is a fifth ladder rule a version replays, and none of its three versions is live
    public static string? Kept(VersionMeasured wider, VersionMeasured narrower) =>
        (wider.Verdict == Crossed, narrower.Verdict == Crossed) switch
        {
            (true, true) => wider.Excess - narrower.Excess >= MarginInPoints ? wider.Version : narrower.Version,
            (true, false) => wider.Version,
            (false, true) => narrower.Version,
            _ => null,
        };
}

// White's Reality Check over the versions of one rule, with the live rule as the
// benchmark.
//
// The best of several versions is not tested against the level a single version
// is tested at: picking the largest of several differences and reading its own
// p-value asks what the chance was of that difference and not what the chance was
// of the largest of this many. One sign vector is applied to every version at
// once rather than one each, because the versions are measured over the same
// blocks of the same nights and flipping them apart would break the dependence
// that makes the best of them large.
// see: A trend version is judged by the candidates' test on its difference from the live rule
public static class RealityCheck
{
    public sealed record Checked(string Best, double Statistic, double PValue, int Challengers);

    public static Checked? Over(IReadOnlyList<VersionMeasured> challengers)
    {
        var read = challengers
            .Where(version => version.Differences.Count >= Blocks.Floor)
            .Select(version => (version.Version, Blocks: (IReadOnlyList<double>)[.. version.Differences.Take(Looks.Maximum)]))
            .ToArray();

        if (read.Length == 0 || read.Select(version => version.Blocks.Count).Distinct().Count() != 1)
        {
            return null;
        }

        var q = read[0].Blocks.Count;
        var statistics = read.Select(version => SignFlip.Statistic(version.Blocks)).ToArray();
        var observed = statistics.Max();
        var best = read[Array.IndexOf(statistics, observed)].Version;
        var flipped = new double[q];
        var atLeast = 0;

        for (var arrangement = 0; arrangement < 1 << q; arrangement++)
        {
            var largest = double.NegativeInfinity;

            foreach (var version in read)
            {
                for (var block = 0; block < q; block++)
                {
                    flipped[block] = (arrangement & (1 << block)) == 0 ? version.Blocks[block] : -version.Blocks[block];
                }

                largest = Math.Max(largest, SignFlip.Statistic(flipped));
            }

            if (SignFlip.AtLeast(largest, observed))
            {
                atLeast++;
            }
        }

        return new Checked(best, observed, (double)atLeast / (1 << q), read.Length);
    }
}
