namespace EquityBrief.Core.Loop;

// One setup an idea's event offers: its stock and session and the catalogue readings its conditions and its order read, by
// column, a reading it does not hold absent.
public sealed record IdeaSetup(string Ticker, DateOnly Session, IReadOnlyDictionary<string, double> Readings);

// One pick an idea walked: its session and its edge after costs per $1 risked against the benchmark, none where its walk
// gave no result.
public readonly record struct IdeaPick(DateOnly Session, double? Edge);

// An idea's figures: its picks and mean edge over the years it is chosen on and over the years it is judged on, and the
// blocks of the judged years its gate reads.
public sealed record IdeaFigures(int EarlyPicks, double? EarlyEdge, int LatePicks, double? LateEdge, IReadOnlyList<(int Block, double Sum)> Blocks);

// What the tester says of one idea of a run: its adjusted p-value where its blocks reach the floor, the bar, its verdict and
// why in words.
public sealed record IdeaVerdict(double? Adjusted, double Bar, string Verdict, string Why);

// The one tester every idea goes through, whatever wrote it. An idea is chosen on 2005 to 2018 alone: the cut points between
// each reading's tenths are read from the event's setups of those years and nothing later, so a later setup's figure moves no
// level. It is judged from 2019 on its edge against the benchmark, summed over blocks of 63 sessions, and every idea of one
// run is read together in one step-down at the run's share of a year's level.
// see: Every idea tested in a run is read together against the run's share of a year's level, and the level stays a year's
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public static class IdeaTester
{
    public const int EarlyThrough = 2018;

    public const int LateFrom = EarlyThrough + 1;

    // The weekly run's share of a year's level: the year's 0.05 over the year's 52 runs.
    public const int RunsAYear = 52;

    public static double Bar => LoopGate.Level / RunsAYear;

    public const string Passed = "passed";

    public const string NearMiss = "near miss";

    public const string Failed = "failed";

    public const string Waiting = "waiting";

    // Each reading's cut points over the event's setups of the early years alone; a reading holding fewer than ten values
    // there has none, and a condition on it holds for no setup.
    public static IReadOnlyDictionary<string, double[]> CutsOf(IEnumerable<IdeaSetup> setups, IEnumerable<string> readings)
    {
        var early = setups.Where(setup => setup.Session.Year <= EarlyThrough).ToArray();
        var cuts = new Dictionary<string, double[]>(StringComparer.Ordinal);

        foreach (var reading in readings.Distinct(StringComparer.Ordinal))
        {
            if (IdeaGrammar.CutsOf(early.Select(setup => setup.Readings.TryGetValue(reading, out var value) ? value : double.NaN)) is { } held)
            {
                cuts[reading] = held;
            }
        }

        return cuts;
    }

    // The setups an idea buys: those holding every condition's band, and where it caps a night, the most of a night's in its
    // order, a setup without the order's reading last and ties by ticker.
    public static IReadOnlyList<IdeaSetup> Selected(IdeaLine idea, IEnumerable<IdeaSetup> setups, IReadOnlyDictionary<string, double[]> cuts)
    {
        var held = setups.Where(setup => idea.Conditions.All(condition =>
            cuts.TryGetValue(condition.Reading, out var levels)
            && IdeaGrammar.Holds(condition, setup.Readings.TryGetValue(condition.Reading, out var value) ? value : null, levels)));

        if (idea.Cap is not { } cap)
        {
            return [.. held.OrderBy(setup => setup.Session).ThenBy(setup => setup.Ticker, StringComparer.Ordinal)];
        }

        double Order(IdeaSetup setup) =>
            setup.Readings.TryGetValue(idea.OrderBy!, out var value) && double.IsFinite(value) ? (idea.Highest ? -value : value) : double.PositiveInfinity;

        return
        [
            .. held.GroupBy(setup => setup.Session).OrderBy(night => night.Key)
                .SelectMany(night => night.OrderBy(Order).ThenBy(setup => setup.Ticker, StringComparer.Ordinal).Take(cap)),
        ];
    }

    // An idea's figures: the mean edge of the early years' picks and of the later years', and the later years' blocks, each
    // session placed by the calendar's count of sessions from the first of 2019, a block counted once the sessions after its
    // last hold the time limit.
    public static IdeaFigures FiguresOf(IReadOnlyList<IdeaPick> picks, Func<DateOnly, int> sessionOf, int newest, int timeLimit)
    {
        var early = picks.Where(pick => pick.Session.Year <= EarlyThrough && pick.Edge is not null).Select(pick => pick.Edge!.Value).ToArray();
        var late = picks.Where(pick => pick.Session.Year >= LateFrom && pick.Edge is not null).ToArray();
        var blocks = LoopGate.Blocks(late.Select(pick => new PairedUnit(sessionOf(pick.Session), pick.Edge, null)), 0, newest, timeLimit);

        return new IdeaFigures(
            early.Length,
            early.Length == 0 ? null : early.Average(),
            late.Length,
            late.Length == 0 ? null : late.Average(pick => pick.Edge!.Value),
            blocks);
    }

    // Every idea of one run read together: their blocks laid over the same blocks, a block an idea held nothing in counting
    // nought for it, each idea's adjusted p-value read in one step-down; an idea under the floor of blocks takes no p-value.
    // An idea passes where it made money on the early years and its adjusted p-value from 2019 is at or under the bar; it is
    // a near miss where it made money on both and stayed over the bar; otherwise it failed.
    public static IReadOnlyList<IdeaVerdict> Verdicts(IReadOnlyList<IdeaFigures> ideas, long draws = 0)
    {
        var read = ideas.Select((idea, at) => (idea, at)).Where(pair => pair.idea.Blocks.Count >= Returns.Blocks.Floor).ToArray();
        var blocks = read.SelectMany(pair => pair.idea.Blocks.Select(block => block.Block)).Distinct().Order().ToArray();
        var adjusted = new double?[ideas.Count];

        if (read.Length > 0)
        {
            IReadOnlyList<IReadOnlyList<double>> sums =
            [
                .. read.Select(pair =>
                {
                    var own = pair.idea.Blocks.ToDictionary(block => block.Block, block => block.Sum);

                    return (IReadOnlyList<double>)[.. blocks.Select(block => own.GetValueOrDefault(block))];
                }),
            ];
            var values = LoopGate.Adjusted(sums, draws);

            for (var at = 0; at < read.Length; at++)
            {
                adjusted[read[at].at] = values[at];
            }
        }

        return [.. ideas.Select((idea, at) => VerdictOf(idea, adjusted[at]))];
    }

    static IdeaVerdict VerdictOf(IdeaFigures idea, double? adjusted)
    {
        var bar = Bar;
        var oneIn = Math.Round(1 / bar);

        if (idea.EarlyEdge is not { } early || early <= 0)
        {
            return new IdeaVerdict(adjusted, bar, Failed, "Made nothing over the benchmark on the years it was chosen on.");
        }

        if (adjusted is not { } p)
        {
            return new IdeaVerdict(null, bar, Failed, FormattableString.Invariant($"Too few blocks from 2019 to judge: {idea.Blocks.Count} of the {Returns.Blocks.Floor} a test reads."));
        }

        if (p <= bar && idea.LateEdge is > 0)
        {
            return new IdeaVerdict(p, bar, Passed, FormattableString.Invariant($"Cleared the bar: reached 1 in {Math.Round(1 / p):0}, the bar 1 in {oneIn:0}."));
        }

        return idea.LateEdge is > 0
            ? new IdeaVerdict(p, bar, NearMiss, FormattableString.Invariant($"Reached 1 in {Math.Round(1 / p):0}, the bar 1 in {oneIn:0}."))
            : new IdeaVerdict(p, bar, Failed, "Passed the early years, above nothing from 2019.");
    }
}
