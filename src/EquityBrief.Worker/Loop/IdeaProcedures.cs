using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Worker.Loop;

// One prompt the idea generator puts for an index: the family's rule, the fold's year, the all-finished fold's the year
// after the newest session's, the prompt and the key its answer is stored under.
public sealed record IdeaAsk(string Family, int Year, string Prompt, string Key);

// The idea generator's proposals as the tester runs them inside each fold, for each family a rule's hooks reach on an
// index: each fold's prompt made from that fold's own tables, the autopsy of the rule's trades that ended before its
// year and each reading's spread over the listings that did, and over all finished data the same; the answer the
// generator stored for the prompt read into its labels, each turned into a condition at its tenth's edge over the same
// listings; the k-th proposal the k-th label each fold's answer holds, the rule walked with it over the fold's year
// against the rule as it stands. A fold whose prompt holds no stored answer, or an answer holding fewer labels, chooses
// none at that rank. It calls no model: the answers are the generator's, stored before the tester runs and read through
// the one reader of the stored answers.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public static class IdeaProcedures
{
    public const string Prefix = "the idea generator, ranked ";

    public static string ProposalName(int rank) => FormattableString.Invariant($"{Prefix}{rank}");

    // One fold's ask: the fold, the prompt its own tables make and its key, and the listings it learned on, which a label's
    // level is read over.
    public sealed record FoldAsk(LoopFold Fold, string Prompt, string Key, IReadOnlyList<ConditionUnit> Units);

    // A fold's ask from what the rule listed and the trades it finished: the listings whose trade ended before the fold's
    // year with their readings, and the trades that ended before it, each read through the fold's view, so nothing of the
    // year it is tested on or after it reaches the prompt; and only the readings the run reads, where it names them.
    public static FoldAsk Ask(
        string family,
        LoopFold fold,
        IReadOnlyList<DateOnly> calendar,
        IReadOnlyList<RuleListing> listings,
        Func<int, IReadOnlyList<double?>> readingsOf,
        IReadOnlyList<(DateOnly Ended, TradePath Path)> finished,
        Func<int, bool>? reads = null)
    {
        var units = ConditionProcedures.LearningUnits(fold, calendar, listings, readingsOf);
        TradePath[] paths = [.. new FoldView<(DateOnly Ended, TradePath Path)>(fold, finished, one => one.Ended, one => one.Ended).Learning.Select(one => one.Path)];
        var prompt = IdeaPrompt.For(family, PathAutopsy.Read(paths), [.. ConditionSearch.Spreads(units).Where(spread => reads?.Invoke(spread.Reading) ?? true)]);

        return new FoldAsk(fold, prompt, IdeaPrompt.Key(prompt), units);
    }

    // Each fold's ask and, last, the ask over all finished data, for a family's rule on an index.
    public static IReadOnlyList<FoldAsk> Asks(LoopRead read, RuleWalk rule, LoopReadings readings) => Asks(read, EngineRule.Of(read, rule, readings), readings.Reads);

    // From 21.5 the same over any family's rule as the engines read it, a book family's setups among them, whose prompt
    // reads no path of a trade.
    public static IReadOnlyList<FoldAsk> Asks(LoopRead read, EngineRule rule, Func<int, bool> reads) =>
        [.. read.Folds.Append(read.AllFinished).Select(fold => Ask(rule.Family, fold, read.Calendar, rule.Listings, rule.ReadingsOf, rule.Finished, reads))];

    // The prompts the generator puts for a family's rule, in the order of its folds.
    public static IReadOnlyList<IdeaAsk> Prompts(LoopRead read, RuleWalk rule, LoopReadings readings) => Prompts(read, EngineRule.Of(read, rule, readings), readings.Reads);

    public static IReadOnlyList<IdeaAsk> Prompts(LoopRead read, EngineRule rule, Func<int, bool> reads) =>
        [.. Asks(read, rule, reads).Select(ask => new IdeaAsk(rule.Family, ask.Fold.Year, ask.Prompt, ask.Key))];

    // The generator's proposals for a family's rule on an index, the answers stored read by their prompts' keys.
    public static IReadOnlyList<LoopProposalRead> Run(LoopRead read, RuleWalk rule, LoopReadings readings, Func<string, IReadOnlyList<IdeaLabel>?> answered, Action<string> progress) =>
        Run(read, EngineRule.Of(read, rule, readings), readings.Reads, answered, progress);

    public static IReadOnlyList<LoopProposalRead> Run(LoopRead read, EngineRule rule, Func<int, bool> reads, Func<string, IReadOnlyList<IdeaLabel>?> answered, Action<string> progress)
    {
        progress(FormattableString.Invariant($"reading the stored ideas for the {rule.Family} on the {DecisionCards.NameOf(read.Index)} in each of its {read.Folds.Count} fold(s) and over all finished data"));

        var walks = new Dictionary<string, IReadOnlyList<(int Entry, double? Edge)>>(StringComparer.Ordinal);

        // The rule's walk with a condition set as the hooks an approval would store, on top of those it stands at, each
        // condition walked once.
        IReadOnlyList<(int Entry, double? Edge)> UnitsUnder(ConditionFound condition)
        {
            var key = condition.Words();

            if (!walks.TryGetValue(key, out var held))
            {
                held = rule.Walk(condition.Parameters(), null);
                walks[key] = held;
            }

            return held;
        }

        return Proposals(read, rule, Asks(read, rule, reads), answered, UnitsUnder);
    }

    // The proposals over each fold's ask, the all-finished ask last: the k-th the k-th label each answer holds, turned
    // into its condition over the ask's own listings and walked; how many folds named the label the all-finished answer
    // names at that rank; and the change an approval would store, read off the all-finished answer's condition.
    public static IReadOnlyList<LoopProposalRead> Proposals(
        LoopRead read,
        string family,
        string current,
        int cap,
        IReadOnlyList<FoldAsk> asks,
        Func<string, IReadOnlyList<IdeaLabel>?> answered,
        Func<ConditionFound, IReadOnlyList<(int Entry, double? Edge)>> unitsUnder,
        IReadOnlyList<(int Entry, double? Edge)> own) =>
        Proposals(
            read,
            new EngineRule(family, current, LoopProcedures.Risks, "listing", 1, cap, [], _ => [], own, (_, _) => [], LoopProcedures.Evidence, LoopProcedures.Reference(read, own), []),
            asks,
            answered,
            unitsUnder);

    static IReadOnlyList<LoopProposalRead> Proposals(
        LoopRead read,
        EngineRule rule,
        IReadOnlyList<FoldAsk> asks,
        Func<string, IReadOnlyList<IdeaLabel>?> answered,
        Func<ConditionFound, IReadOnlyList<(int Entry, double? Edge)>> unitsUnder)
    {
        var (family, current, cap, own) = (rule.Family, rule.Current, rule.Cap, rule.Own);
        (IdeaLabel Label, ConditionFound Condition)? AtRank(FoldAsk ask, int place) =>
            answered(ask.Key) is { } labels && place < labels.Count && IdeaPrompt.Condition(labels[place], ask.Units) is { } condition
                ? (labels[place], condition)
                : null;

        var finished = asks[^1];
        var ownEdge = finished.Units.Count > 0 ? finished.Units.Average(unit => unit.Edge) : 0;
        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= IdeaPrompt.MostIdeas; rank++)
        {
            var place = rank - 1;
            var chosen = read.Folds.Select((_, at) => AtRank(asks[at], place)).ToArray();
            var named = AtRank(finished, place);

            proposals.Add(new LoopProposalRead(
                family,
                ProposalName(rank),
                named is { } one ? ConditionProcedures.ChangeOf(one.Condition) : null,
                current,
                rule.Unit,
                cap,
                [.. read.Folds.Select((fold, at) => (fold, chosen[at] is { } choice ? ConditionProcedures.ChangeOf(choice.Condition) : null))],
                named is { } same ? chosen.Count(choice => choice is { } made && made.Label == same.Label) : 0,
                rule.Evidence(read.Folds, read.Calendar, [.. chosen.Select(choice => choice is { } made ? unitsUnder(made.Condition) : null)], own))
            {
                Finding = named is { } found
                    ? FormattableString.Invariant(
                        $"the local model named {IdeaPrompt.Words(found.Label)} over the {finished.Units.Count} finished {rule.Thing}s; {found.Condition.Words()} kept {found.Condition.Kept} of them at {found.Condition.Score * rule.Scale:+0.000;-0.000} {rule.Unit} a {rule.Thing} against the rule's {ownEdge * rule.Scale:+0.000;-0.000}")
                    : null,
                Change = named is { } change ? LoopChange.OfHooks(change.Condition.Parameters()) : null,
                Reference = rule.Reference,
            });
        }

        return proposals;
    }
}
