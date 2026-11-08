namespace EquityBrief.Core.Loop;

// One proposal's evidence over the test span: whether its unit is a book's month, its paired units, each test year's
// pair, and each side's results over the span, a trade's or a month's edge after costs.
public sealed record LoopEvidence(
    bool Book,
    IReadOnlyList<PairedUnit> Units,
    IReadOnlyList<LoopYear> Years,
    IReadOnlyList<double> CurrentResults,
    IReadOnlyList<double> ProposedResults);

// The tester's verdict on the proposals tested for one rule in one run, read together: each proposal's blocks over the
// blocks any of them holds, a block a proposal holds nothing in summing to nothing for it, and the step-down over them
// where they hold the block floor; then each proposal's three screens on its own.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public static class LoopJudge
{
    // The proposals' verdicts in the order given: the test span opens on a session, the history's newest is another, and
    // a block is read once the cap's sessions have traded after its last.
    public static IReadOnlyList<LoopVerdict> Judge(IReadOnlyList<LoopEvidence> proposals, int opens, int newest, int cap, long draws = 0)
    {
        var blocks = proposals.Select(proposal => LoopGate.Blocks(proposal.Units, opens, newest, cap)).ToArray();
        var held = blocks.SelectMany(own => own.Select(block => block.Block)).Distinct().Order().ToArray();
        var sums = blocks
            .Select(own => (IReadOnlyList<double>)[.. held.Select(block => own.FirstOrDefault(one => one.Block == block).Sum)])
            .ToArray();
        var adjusted = held.Length >= Returns.Blocks.Floor ? LoopGate.Adjusted(sums, draws) : null;

        return
        [
            .. proposals.Select((proposal, at) =>
            {
                var (stable, counted, better) = LoopScreens.Stability(proposal.Years, LoopScreens.Fewest(proposal.Book));
                var units = proposal.Book ? proposal.Units.Count : proposal.ProposedResults.Count;
                double? p = adjusted is null ? null : adjusted[at];

                return new LoopVerdict(
                    p,
                    held.Length,
                    p is { } value && value <= LoopGate.Bar,
                    stable,
                    counted,
                    better,
                    LoopScreens.Trimmed(proposal.CurrentResults, proposal.ProposedResults),
                    units,
                    LoopScreens.Counts(proposal.Book, units),
                    LoopGate.Detectable(sums[at], units));
            }),
        ];
    }

    // From 21.17 the evidence of a rule or a proposal reading a release reading as it is judged, on the test years from the
    // local model's cutoff's year alone: its units entered from that year, its test years from it and each side's results
    // over those units. The years before stay in the evidence it is printed from, beside its verdict.
    // see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
    public static LoopEvidence From(LoopEvidence evidence, IReadOnlyList<DateOnly> calendar, int year)
    {
        PairedUnit[] units = [.. evidence.Units.Where(unit => calendar[unit.Session].Year >= year)];

        return new LoopEvidence(
            evidence.Book,
            units,
            [.. evidence.Years.Where(one => one.Year >= year)],
            [.. units.Where(unit => unit.Current is not null).Select(unit => unit.Current!.Value)],
            [.. units.Where(unit => unit.Proposed is not null).Select(unit => unit.Proposed!.Value)]);
    }

    // Each test year's pair from the units entered in it: a unit counts for a side where that side holds a result in it.
    public static IReadOnlyList<LoopYear> Years(IReadOnlyList<LoopFold> folds, IReadOnlyList<DateOnly> calendar, IReadOnlyList<PairedUnit> units) =>
    [
        .. folds.Select(fold =>
        {
            var inYear = units.Where(unit => LoopFolds.Tests(fold, calendar[unit.Session])).ToArray();

            return new LoopYear(
                fold.Year,
                fold.Complete,
                inYear.Count(unit => unit.Current is not null),
                inYear.Count(unit => unit.Proposed is not null),
                inYear.Sum(unit => unit.Current ?? 0),
                inYear.Sum(unit => unit.Proposed ?? 0));
        }),
    ];
}
