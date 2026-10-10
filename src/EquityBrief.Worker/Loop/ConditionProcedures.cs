using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Worker.Loop;

// Winners against losers as the tester runs it inside each fold, for each family a rule's hooks reach on an index:
// every listing the rule makes over the whole history, its edge had it been taken and the catalogue's readings of it
// on its session; each fold searches the listings that ended before its year began, each reading cut at its deciles
// either side and the pair beneath the best, held to a within-night shuffle of its own search, with the family floors
// in proportion to its learning years; the k-th proposal is the k-th condition each fold keeps, the rule walked with it
// over the fold's year against the rule as it stands; and each reading's spread over every finished listing.
// see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public static class ConditionProcedures
{
    // The most conditions the engine proposes a family an index.
    public const int Proposals = 5;

    public static string ProposalName(int rank) => FormattableString.Invariant($"winners against losers, ranked {rank}");

    public static string ChangeOf(ConditionFound condition) => "also requires " + condition.Words();

    public static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<ReadingSpread> Spreads) Run(LoopRead read, RuleWalk rule, LoopReadings readings, Action<string> progress)
    {
        progress(FormattableString.Invariant($"reading the {rule.Family}'s {rule.Listings.Count} listing(s) on the {DecisionCards.NameOf(read.Index)} for winners against losers"));

        IReadOnlyList<double?> ReadingsOf(int at) => readings.Of(rule.Listings[at].Name, rule.Listings[at].Bar, rule.Listings[at].Session);

        IReadOnlyList<ConditionUnit> Learning(LoopFold fold) => LearningUnits(fold, read.Calendar, rule.Listings, ReadingsOf);

        ConditionVerdict Judged(LoopFold fold) => ConditionSearch.Judge(Learning(fold), LoopProcedures.Floors(read.LearningYears(fold)).Trades);

        var verdicts = read.Folds.Select(Judged).ToArray();
        var finishedUnits = Learning(read.AllFinished);
        var finished = Judged(read.AllFinished);
        var spreads = ConditionSearch.Spreads(finishedUnits);
        var walks = new Dictionary<string, IReadOnlyList<(int Entry, double? Edge)>>(StringComparer.Ordinal);

        // The rule's walk with a condition set as the hooks an approval would store, on top of those it stands at, each
        // condition walked once.
        IReadOnlyList<(int Entry, double? Edge)> UnitsUnder(ConditionFound condition)
        {
            var key = condition.Words();

            if (!walks.TryGetValue(key, out var held))
            {
                held = [.. rule.Hooked(condition.Parameters(), ReadingsOf).Select(one => (one.Entry, one.Edge))];
                walks[key] = held;
            }

            return held;
        }

        IReadOnlyList<(int Entry, double? Edge)> own = [.. rule.Own.Select(one => (one.Entry, one.Edge))];
        var ownEdge = finishedUnits.Count > 0 ? finishedUnits.Average(unit => unit.Edge) : 0;
        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= Proposals; rank++)
        {
            var place = rank - 1;
            var chosen = verdicts.Select(verdict => place < verdict.Passing.Count ? verdict.Passing[place] : null).ToArray();
            var named = place < finished.Passing.Count ? finished.Passing[place] : null;

            proposals.Add(new LoopProposalRead(
                rule.Family,
                ProposalName(rank),
                named is null ? null : ChangeOf(named),
                rule.Current,
                LoopProcedures.Risks,
                rule.Cap,
                [.. read.Folds.Select((fold, at) => (fold, chosen[at] is { } one ? ChangeOf(one) : null))],
                named is null ? 0 : chosen.Count(one => one is not null && one.Words() == named.Words()),
                LoopProcedures.Evidence(read.Folds, read.Calendar, [.. chosen.Select(one => one is null ? null : UnitsUnder(one))], own))
            {
                Finding = named is null ? null : Finding(named, finished, spreads, finishedUnits.Count, ownEdge),
                Change = named is null ? null : LoopChange.OfHooks(named.Parameters()),
                Reference = LoopProcedures.Reference(read, own),
            });
        }

        return (proposals, spreads);
    }

    // The listings a fold learns on, those whose trade ended before its year began, each with its edge and its readings,
    // read through the fold's view so no listing of the year or after it is read.
    public static IReadOnlyList<ConditionUnit> LearningUnits(LoopFold fold, IReadOnlyList<DateOnly> calendar, IReadOnlyList<RuleListing> listings, Func<int, IReadOnlyList<double?>> readingsOf) =>
    [
        .. new FoldView<int>(fold, [.. Enumerable.Range(0, listings.Count)], at => calendar[listings[at].Session], at => listings[at].Exit is { } exit ? calendar[exit] : null)
            .Learning
            .Where(at => listings[at].Edge is not null)
            .Select(at => new ConditionUnit(listings[at].Session, listings[at].Edge!.Value, readingsOf(at))),
    ];

    // A condition's finding: what it kept of every finished listing at what edge against the rule's own, the null's
    // mark it stood above, and its first reading's medians among the winners and the losers.
    public static string Finding(ConditionFound condition, ConditionVerdict verdict, IReadOnlyList<ReadingSpread> spreads, int listings, double ownEdge)
    {
        var first = condition.Conditions[0];
        var spread = spreads.First(one => one.Reading == first.Reading);

        return FormattableString.Invariant(
            $"{condition.Words()} kept {condition.Kept} of the {listings} finished listings at {condition.Score:+0.000;-0.000} risks a listing against the rule's {ownEdge:+0.000;-0.000}, above the within-night null's 95th percentile of {verdict.Mark:+0.000;-0.000} over {verdict.Arrangements} arrangements")
            + (spread.WinnersMedian is { } winners && spread.LosersMedian is { } losers
                ? FormattableString.Invariant($"; the winners' median {spread.Column} {winners:0.####} against the losers' {losers:0.####}")
                : string.Empty);
    }
}
