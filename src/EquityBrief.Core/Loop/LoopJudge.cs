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
    public static IReadOnlyList<LoopVerdict> Judge(IReadOnlyList<LoopEvidence> proposals, int opens, int newest, int cap)
    {
        var blocks = proposals.Select(proposal => LoopGate.Blocks(proposal.Units, opens, newest, cap)).ToArray();
        var held = blocks.SelectMany(own => own.Select(block => block.Block)).Distinct().Order().ToArray();
        var sums = blocks
            .Select(own => (IReadOnlyList<double>)[.. held.Select(block => own.FirstOrDefault(one => one.Block == block).Sum)])
            .ToArray();
        var adjusted = held.Length >= Returns.Blocks.Floor ? LoopGate.Adjusted(sums) : null;

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
