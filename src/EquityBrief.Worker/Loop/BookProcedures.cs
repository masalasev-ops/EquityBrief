using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// From 21.5 the two book families on an index, Quality at a fair price and the Fundamentals family, as the tester's engines
// read them inside each fold. The book menu: each change walked by the book's own walk over the whole history, each fold
// ranking the changes above the rule's own book on the months that ended before its year, at the family's month floors in
// proportion to the years it learns on, the k-th proposal the k-th change each fold ranks, scored on that fold's year
// against the rule's own book, a change no dial of the rule reads shown and never put. And the rule as the engines that
// propose a change of its hooks read it: the ledger's settled setups of the family on the index as what they search, each
// one's readings the catalogue's on its session, and its walk the book's own with a change's hooks keeping and ordering the
// members each session chooses, as the night's hooks do, its months the unit.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
// see: A change a family's night does not read is tested and shown and never put to the operator
public static class BookProcedures
{
    // One change of a book's menu: its words, its book's months, and the change an approval would register, none for a
    // sale no dial of the rule reads.
    public sealed record MenuChange(string Words, IReadOnlyList<(int Opens, double? Edge)> Months, LoopChange? Change);

    // Why a change of a book's menu is not put.
    public const string NotRead = "shown and not put to you, since the family's night reads no such sale";

    // A book's months a fold learns on, those whose close, the session the next month opens on, falls before its year, with
    // the family's month floors read over them and their mean edge, none where none is held.
    public static (QualityMonths Months, double? Edge) Learned(IReadOnlyList<(int Opens, double? Edge)> months, IReadOnlyList<int> starts, IReadOnlyList<DateOnly> calendar, LoopFold fold)
    {
        var closes = starts.Zip(starts.Skip(1)).ToDictionary(pair => pair.First, pair => pair.Second);
        var learned = months
            .Where(month => closes.TryGetValue(month.Opens, out var close) && LoopFolds.Learns(fold, calendar[close]))
            .ToArray();
        var edges = learned.Where(month => month.Edge is not null).Select(month => month.Edge!.Value).ToArray();

        return (QualityRule.Months(learned.Select(month => (calendar[month.Opens], month.Edge)), SweepColumns.FirstScored.Year), edges.Length > 0 ? edges.Average() : null);
    }

    public static IReadOnlyList<LoopProposalRead> Menu(
        LoopRead read,
        string family,
        string current,
        IReadOnlyList<(int Opens, double? Edge)> own,
        IReadOnlyList<int> starts,
        IReadOnlyList<MenuChange> menu,
        Action<string> progress)
    {
        progress(FormattableString.Invariant($"walking the {family} on the {DecisionCards.NameOf(read.Index)} under its own exits and the book menu's {menu.Count}"));

        IReadOnlyList<(int At, double Edge)> Ranked(LoopFold fold)
        {
            var years = read.LearningYears(fold);
            var ownEdge = Learned(own, starts, read.Calendar, fold).Edge;

            return
            [
                .. menu
                    .Select((change, at) => (At: at, Learned: Learned(change.Months, starts, read.Calendar, fold)))
                    .Where(one => one.Learned.Edge is { } edge && QualityRule.MeetsFloors(one.Learned.Months, years) && (ownEdge is null || edge > ownEdge))
                    .OrderByDescending(one => one.Learned.Edge)
                    .ThenBy(one => one.At)
                    .Select(one => (one.At, one.Learned.Edge!.Value)),
            ];
        }

        var ranked = read.Folds.Select(Ranked).ToArray();
        var finished = Ranked(read.AllFinished);
        var ownFinished = Learned(own, starts, read.Calendar, read.AllFinished).Edge ?? 0;
        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= menu.Count; rank++)
        {
            var place = rank - 1;
            int?[] chosen = [.. ranked.Select(fold => place < fold.Count ? fold[place].At : (int?)null)];
            var named = place < finished.Count ? finished[place] : ((int At, double Edge)?)null;

            proposals.Add(new LoopProposalRead(
                family,
                ExitProcedures.ProposalName(rank),
                named is { } exit ? "exit: " + menu[exit.At].Words : null,
                current,
                LoopProcedures.Points,
                BookMonths.LongestMonth,
                [.. read.Folds.Select((fold, at) => (fold, chosen[at] is { } choice ? "exit: " + menu[choice].Words : null))],
                named is { } best ? chosen.Count(choice => choice == best.At) : 0,
                LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(choice => choice is { } at ? menu[at].Months : null)], own))
            {
                Finding = named is { } found
                    ? FormattableString.Invariant($"{menu[found.At].Words} read {found.Edge * 100:+0.00;-0.00} points a month on the years it learned on against the rule's own {ownFinished * 100:+0.00;-0.00}")
                        + (menu[found.At].Change is null ? "; " + NotRead : string.Empty)
                    : null,
                Change = named is { } kept ? menu[kept.At].Change : null,
            });
        }

        return proposals;
    }

    // A book family's live rule as the engines proposing a change of its hooks read it: the ledger's settled setups of the
    // family on the index, each one's readings the catalogue's on its session, its book's months its units, and its walk the
    // book's own with a change's hooks on top of those it stands at keeping and ordering the members each session chooses.
    public static EngineRule Engine(
        LoopRead read,
        string family,
        string current,
        IReadOnlyList<RuleListing> setups,
        LoopReadings readings,
        IReadOnlyDictionary<string, double> standing,
        Func<Func<int, IReadOnlyList<int>, IReadOnlyList<int>>?, IReadOnlyList<(int Opens, double? Edge)>> months,
        Func<long, TreeModel?>? trees = null)
    {
        IReadOnlyList<(int Entry, double? Edge)> Walk(IReadOnlyDictionary<string, double> change, TreeModel? fitted) =>
            months(Arranged(read, readings, RuleWalk.Composed(change, standing).With(key => fitted is not null && fitted.Key == key ? fitted : trees?.Invoke(key))));

        return new EngineRule(
            family,
            RuleHooks.Of(standing).Words() is { } hooked ? current + "; " + hooked : current,
            LoopProcedures.Points,
            "setup",
            100,
            BookMonths.LongestMonth,
            setups,
            at => readings.Of(setups[at].Name, setups[at].Bar, setups[at].Session),
            Walk(LoopChange.NoHooks, null),
            Walk,
            LoopProcedures.BookEvidence,
            null,
            []);
    }

    // The arrangement hooks set on a book's walk: each session's chosen members kept and ordered by them over the
    // catalogue's readings on that session, as the night's hooks keep and order them; none where they read no reading.
    public static Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? Arranged(LoopRead read, LoopReadings readings, RuleHooks hooks)
    {
        IReadOnlyList<double?>? ReadingsOn(int name, int session) =>
            Array.BinarySearch(read.Series[name].SessionAt, session) is var bar && bar >= 0 ? readings.Of(name, bar, session) : null;

        return hooks.ReadsReadings ? (session, chosen) => hooks.Order(chosen, name => ReadingsOn(name, session)) : null;
    }
}
