using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Books;

// One position of a book's own exit: the stock, the night the walk chose it or found it held, where it ended and why, and
// its change in the book's own measure to its sale or to the newest night the walk read, none where it is not yet bought.
public sealed record OwnExitPosition(string Ticker, DateOnly ChosenOn, DateOnly? EndedOn, string? EndReason, double? Change);

// A book's own exit walked from the night its live exit first applied: every position it held or chose since, and the mean
// change over them, the open ones read at the newest night.
public sealed record OwnExitRecord(DateOnly Since, IReadOnlyList<OwnExitPosition> Positions)
{
    public IReadOnlyList<OwnExitPosition> Held { get; } = [.. Positions.Where(position => position.EndedOn is null)];

    public IReadOnlyList<OwnExitPosition> Sold { get; } = [.. Positions.Where(position => position.EndedOn is not null)];

    public double? MeanChange { get; } = Positions.Where(position => position.Change is not null).Select(position => position.Change!.Value).ToArray() is { Length: > 0 } changes
        ? changes.Average()
        : null;
}

// A position the walk starts from: a live holding open on the evening before the first night under its live exit, with the
// growth its stored answer carried on the night it was chosen and the gap to its fill, none until it is bought.
public sealed record OwnExitStart(string Ticker, DateOnly ChosenOn, double StartCarried, double? Gap);

// A member's stored answer on a night, as the book's step wrote it: whether it qualifies and its place in the order for
// Quality at a fair price, its scorecard for the Fundamentals family, and the growth carried on it.
public sealed record OwnExitAnswer(bool Passed, int? Place, int PassedChecks, int Applied, double Carried);

// A night the walk reads: the members' stored answers, whether it reshuffled, and for the Fundamentals family the stocks the
// live book bought on it.
public sealed record OwnExitNight(DateOnly Night, bool Reshuffle, IReadOnlyDictionary<string, OwnExitAnswer> Answers, IReadOnlyList<string> Bought);

// A heavyweights night: on a rebalance the stocks the live book bought at its close and, for a rule selling on the lead, the
// leaders the rebalance stored; between rebalances neither.
public sealed record OwnExitRebalance(DateOnly Night, IReadOnlyList<string>? Bought, IReadOnlySet<string>? Leaders);

// A heavyweight the walk starts from: a live holding open before the first night under its live exit, bought at a close.
public sealed record OwnExitHolding(string Ticker, DateOnly EnteredOn);

// The books' own exit as a comparison record, on the operator's ruling of 2026-11-16: each book walked from the night its live
// exit first applied as it sold before the rules restart, at its reshuffle or its rebalance and on its own sales, never at a
// stop or a take-profit, from the answers its step stored each night. It registers nothing and draws no pick.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public static class OwnExit
{
    public const string NoLongerQualifies = "no longer qualifies";
    public const string NoLongerTheLeader = "no longer the leader";
    public const string LeftTheIndex = "left the index";
    public const string UnderItsAverage = "under its 200-day average";

    sealed class Open(string ticker, DateOnly chosenOn, double startCarried, double? gap, DateOnly through, double carried)
    {
        public string Ticker { get; } = ticker;
        public DateOnly ChosenOn { get; } = chosenOn;
        public double StartCarried { get; } = startCarried;
        public double? Gap { get; set; } = gap;
        public DateOnly Through { get; set; } = through;
        public double Carried { get; set; } = carried;

        // The book's own measure: its growth from its choice's close less one less its gap to the fill.
        public double? Change => Gap is { } gap && StartCarried > 0 ? (Carried / StartCarried) - 1.0 - gap : null;

        public OwnExitPosition Ended(DateOnly on, string reason) => new(Ticker, ChosenOn, on, reason, Change);

        public OwnExitPosition Held() => new(Ticker, ChosenOn, null, null, Change);
    }

    // Quality at a fair price: a holding is sold at a reshuffle whose stored answer says it no longer qualifies and at its
    // last close as a member, and a reshuffle fills the places left from its stored order of the qualifying members, as the
    // step fills them; where the rule reads it, one closing under its 200-day average is sold at that close.
    public static OwnExitRecord Quality(
        DateOnly since,
        int holdings,
        IReadOnlyList<OwnExitStart> starts,
        IReadOnlyList<OwnExitNight> nights,
        Func<string, DateOnly, bool> member,
        Func<string, DateOnly, double?> gapOf,
        Func<string, DateOnly, bool>? underAverage = null)
    {
        var open = starts.Select(start => new Open(start.Ticker, start.ChosenOn, start.StartCarried, start.Gap, start.ChosenOn, start.StartCarried)).ToList();
        var ended = new List<OwnExitPosition>();

        foreach (var night in nights.Where(night => night.Night >= since).OrderBy(night => night.Night))
        {
            Carry(open, night, gapOf);
            Leave(open, ended, night.Night, member);

            if (underAverage is not null)
            {
                foreach (var position in open.Where(position => position.Through == night.Night && position.ChosenOn < night.Night && underAverage(position.Ticker, night.Night)).ToArray())
                {
                    open.Remove(position);
                    ended.Add(position.Ended(night.Night, UnderItsAverage));
                }
            }

            if (!night.Reshuffle)
            {
                continue;
            }

            foreach (var position in open.Where(position => night.Answers.TryGetValue(position.Ticker, out var answer) && !answer.Passed).ToArray())
            {
                open.Remove(position);
                ended.Add(position.Ended(night.Night, NoLongerQualifies));
            }

            var places = holdings - open.Count;

            foreach (var (ticker, answer) in night.Answers
                .Where(pair => pair.Value is { Passed: true, Place: not null } && open.All(position => position.Ticker != pair.Key))
                .OrderBy(pair => pair.Value.Place)
                .Take(Math.Max(0, places)))
            {
                open.Add(new Open(ticker, night.Night, answer.Carried, gapOf(ticker, night.Night), night.Night, answer.Carried));
            }
        }

        return new OwnExitRecord(since, [.. ended, .. open.Select(position => position.Held())]);
    }

    // The Fundamentals family: the same buys as the live book, a stock the walk still holds not bought again, each sold where
    // its stored scorecard stands under the hold floor, at its last session under the time limit counted in the sessions it
    // was held, at its last close as a member, and where the rule reads it at a close under its 200-day average.
    public static OwnExitRecord Improving(
        DateOnly since,
        ImprovingSettings settings,
        IReadOnlyList<OwnExitStart> starts,
        IReadOnlyList<OwnExitNight> nights,
        Func<string, DateOnly, bool> member,
        Func<string, DateOnly, double?> gapOf,
        Func<string, DateOnly, DateOnly, int> sessionsHeld,
        Func<string, DateOnly, bool>? underAverage = null)
    {
        var open = starts.Select(start => new Open(start.Ticker, start.ChosenOn, start.StartCarried, start.Gap, start.ChosenOn, start.StartCarried)).ToList();
        var ended = new List<OwnExitPosition>();

        foreach (var night in nights.Where(night => night.Night >= since).OrderBy(night => night.Night))
        {
            Carry(open, night, gapOf);
            Leave(open, ended, night.Night, member);

            foreach (var position in open.Where(position => position.ChosenOn < night.Night).ToArray())
            {
                var answered = night.Answers.TryGetValue(position.Ticker, out var answer);
                var fell = answered && answer!.Applied > 0 && !ImprovingRule.Holds(answer.PassedChecks, answer.Applied, settings);
                var timed = !fell && answered && position.Gap is not null && sessionsHeld(position.Ticker, position.ChosenOn, night.Night) >= settings.Sessions;
                var below = !fell && !timed && answered && underAverage is not null && underAverage(position.Ticker, night.Night);

                if (fell || timed || below)
                {
                    open.Remove(position);
                    ended.Add(position.Ended(night.Night, fell ? ImprovingRule.ScoreFell : timed ? ImprovingRule.TimeLimit : UnderItsAverage));
                }
            }

            foreach (var ticker in night.Bought.Where(ticker => open.All(position => position.Ticker != ticker) && night.Answers.ContainsKey(ticker)))
            {
                var answer = night.Answers[ticker];

                open.Add(new Open(ticker, night.Night, answer.Carried, gapOf(ticker, night.Night), night.Night, answer.Carried));
            }
        }

        return new OwnExitRecord(since, [.. ended, .. open.Select(position => position.Held())]);
    }

    // The sector heavyweights: a holding is kept to the rebalance whose stored leaders leave it out, where the rule sells on
    // that, and sold at its last close as a member and, where the rule reads it, at a close under its 200-day average; a
    // rebalance buys at that close each stock the live book bought on it that the walk does not hold, the live book buying
    // each leader it does not hold and the walk holding every stock the live book holds. Its change is its close over its
    // buy's, both as the store holds them now, less one.
    public static OwnExitRecord Heavyweights(
        DateOnly since,
        bool soldOnLeading,
        IReadOnlyList<OwnExitHolding> starts,
        IReadOnlyList<OwnExitRebalance> nights,
        Func<string, DateOnly, bool> member,
        Func<string, DateOnly, decimal?> closeOn,
        Func<string, DateOnly, bool>? underAverage = null)
    {
        var open = starts.Select(start => (start.Ticker, start.EnteredOn, Through: start.EnteredOn)).ToList();
        var ended = new List<OwnExitPosition>();

        double? ChangeOf(string ticker, DateOnly entered, DateOnly through) =>
            closeOn(ticker, entered) is { } bought && bought > 0m && closeOn(ticker, through) is { } now ? Statistic.FromRatio(now / bought) - 1.0 : null;

        foreach (var night in nights.Where(night => night.Night >= since).OrderBy(night => night.Night))
        {
            for (var at = 0; at < open.Count; at++)
            {
                if (closeOn(open[at].Ticker, night.Night) is not null)
                {
                    open[at] = open[at] with { Through = night.Night };
                }
            }

            foreach (var holding in open.Where(holding => !member(holding.Ticker, night.Night)).ToArray())
            {
                open.Remove(holding);
                ended.Add(new OwnExitPosition(holding.Ticker, holding.EnteredOn, holding.Through, LeftTheIndex, ChangeOf(holding.Ticker, holding.EnteredOn, holding.Through)));
            }

            if (underAverage is not null)
            {
                foreach (var holding in open.Where(holding => holding.Through == night.Night && holding.EnteredOn < night.Night && underAverage(holding.Ticker, night.Night)).ToArray())
                {
                    open.Remove(holding);
                    ended.Add(new OwnExitPosition(holding.Ticker, holding.EnteredOn, night.Night, UnderItsAverage, ChangeOf(holding.Ticker, holding.EnteredOn, night.Night)));
                }
            }

            if (soldOnLeading && night.Leaders is { } leaders)
            {
                foreach (var holding in open.Where(holding => holding.EnteredOn < night.Night && !leaders.Contains(holding.Ticker)).ToArray())
                {
                    open.Remove(holding);
                    ended.Add(new OwnExitPosition(holding.Ticker, holding.EnteredOn, night.Night, NoLongerTheLeader, ChangeOf(holding.Ticker, holding.EnteredOn, night.Night)));
                }
            }

            foreach (var bought in (night.Bought ?? []).Where(bought => open.All(holding => holding.Ticker != bought)))
            {
                open.Add((bought, night.Night, night.Night));
            }
        }

        return new OwnExitRecord(since, [.. ended, .. open.Select(holding => new OwnExitPosition(holding.Ticker, holding.EnteredOn, null, null, ChangeOf(holding.Ticker, holding.EnteredOn, holding.Through)))]);
    }

    // Each open position's growth carried to the night where the night answered it, and its gap read once it is bought.
    static void Carry(List<Open> open, OwnExitNight night, Func<string, DateOnly, double?> gapOf)
    {
        foreach (var position in open)
        {
            if (night.Answers.TryGetValue(position.Ticker, out var answer))
            {
                position.Carried = answer.Carried;
                position.Through = night.Night;
            }

            position.Gap ??= gapOf(position.Ticker, position.ChosenOn);
        }
    }

    // A position whose stock is no longer a member is sold at the last night it was carried to, its last close as one.
    static void Leave(List<Open> open, List<OwnExitPosition> ended, DateOnly night, Func<string, DateOnly, bool> member)
    {
        foreach (var position in open.Where(position => !member(position.Ticker, night)).ToArray())
        {
            open.Remove(position);
            ended.Add(position.Ended(position.Through, LeftTheIndex));
        }
    }
}
