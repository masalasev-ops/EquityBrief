using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;

namespace EquityBrief.Worker.Loop;

// The shape clock's moves as proposals of a run on the S&P 500: each lever of the newest shape proposal written for the
// open filter version, one gate's setting a move, walked at the moved setting by value over the same history and tested
// on each test year against the filter as it stands, the same hooks stood at by both. The clock chose the move from the
// nights' counts and read no outcome, so every fold reads the same move, a test of the move itself.
// see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
public static class ShapeProcedures
{
    // A move's name among a run's proposals.
    public static string ProposalName(string setting) => "the shape clock's move of the " + setting;

    // The moves a stored proposal's levers make: each gate's setting a value the clock proposed other than the one held.
    public static IReadOnlyList<(string Setting, double Proposed)> MovesOf(string levers)
    {
        var read = JsonSerializer.Deserialize<Lever[]>(levers) ?? [];

        return
        [
            .. read
                .Where(lever => lever.Setting is not null && lever.Proposed is { } proposed && lever.Current is { } held && proposed != held)
                .Select(lever => (lever.Setting!, lever.Proposed!.Value)),
        ];
    }

    // Each move a proposal, its test years read against the filter as it stands.
    public static IReadOnlyList<LoopProposalRead> Run(
        LoopRead read,
        RuleWalk current,
        Func<FilterSettings, RuleWalk> walkAt,
        FilterSettings settings,
        IReadOnlyList<(string Setting, double Proposed)> moves,
        long proposal,
        Action<string> progress)
    {
        static IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<ExitProcedures.Walked> trades) => [.. trades.Select(one => (one.Entry, one.Edge))];

        var own = Units(current.Own);
        var proposals = new List<LoopProposalRead>();

        foreach (var (setting, value) in moves)
        {
            var change = LoopChange.OfShape(setting, value);
            var moved = walkAt(LoopChange.Moved(settings, change.Shape!));
            var units = Units(moved.Own);
            var words = change.Words();

            progress(FormattableString.Invariant($"walking the shape clock's move, {words}, over {moved.Listings.Count} listing(s)"));

            proposals.Add(new LoopProposalRead(
                current.Family,
                ProposalName(setting),
                words,
                current.Current,
                LoopProcedures.Risks,
                current.Cap,
                [.. read.Folds.Select(fold => (fold, (string?)words))],
                read.Folds.Count,
                LoopProcedures.Evidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => units)], own))
            {
                Finding = string.Create(CultureInfo.InvariantCulture, $"shape proposal {proposal} moves the {setting} from where the filter holds it to {value:0.##}, the nearest value bringing its gate's median count over the ordinary nights inside its band"),
                Change = change,
                Reference = LoopProcedures.Reference(read, own),
            });
        }

        return proposals;
    }
}
