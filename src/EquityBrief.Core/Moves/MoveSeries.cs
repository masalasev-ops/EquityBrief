using EquityBrief.Core.Prices;
namespace EquityBrief.Core.Moves;

// One session as the move arithmetic reads it. Close alone, because a move is a
// change in the close and nothing here asks what happened inside a day.
public readonly record struct MoveBar(DateOnly SessionDate, decimal Close);

// One selected move: the session it ended on, how many sessions it spans, and
// what it changed by.
//
// `ChangePct` is a statistic and is double, because a percentage of a price is
// not a price. `Sessions` is 1 for a single day and more for a run.
public sealed record Move(DateOnly SessionDate, int Sessions, double ChangePct, int Rank);

// The largest single-day and multi-day moves of a stored series.
//
// A pure function of a session-ordered series, as every other stage's arithmetic
// is, so the component reads and writes and this decides.
// see: Code owns every number
public static class MoveSeries
{
    // The spans a move is measured over: one session, and one trading week.
    //
    // Two rather than a range, because a set of spans is a threshold and nothing
    // has measured one. One session is what the catalogue means by a single-day
    // move; five is a week, which is the shortest span over which a run reads as
    // a move rather than as two days that happened to agree. A third span would
    // be a figure nobody could later tell from a measured one, which is the rule
    // the tranche lookback and the shock multiple are already stated under.
    public static readonly int[] Spans = [1, 5];

    // How many moves a name keeps. The how-it-got-here table is read down, and a
    // table of the whole year's sessions is not a table of the biggest moves.
    public const int MostMoves = 8;

    public static IReadOnlyList<Move> For(IReadOnlyList<MoveBar> bars)
    {
        if (bars.Count < 2)
        {
            return [];
        }

        // One candidate per session per span, keyed on the session it ended on,
        // because that is the grain the store has. Where two spans end on the
        // same session the longer one wins, which is what `SCHEMA.md` states and
        // is the reading that keeps the primary key: a five-day run and the
        // single day inside it are one event and the run is the bigger claim
        // about it.
        var best = new Dictionary<DateOnly, Move>();

        foreach (var span in Spans.OrderBy(span => span))
        {
            for (var at = span; at < bars.Count; at++)
            {
                var from = bars[at - span].Close;

                if (from <= 0)
                {
                    continue;
                }

                var change = Statistic.FromRatio((bars[at].Close - from) / from) * 100;
                var ending = bars[at].SessionDate;

                if (!best.TryGetValue(ending, out var held) || span > held.Sessions)
                {
                    best[ending] = new Move(ending, span, change, 0);
                }
            }
        }

        // Ranked by absolute size, because a fall is as much of a move as a rise
        // and the table is about what happened rather than about which way. Ties
        // break on the later session first, so a rank is a total order rather
        // than whatever order the dictionary yields.
        return
        [
            .. best.Values
                .OrderByDescending(move => Math.Abs(move.ChangePct))
                .ThenByDescending(move => move.SessionDate)
                .Take(MostMoves)
                .Select((move, at) => move with { Rank = at + 1 }),
        ];
    }
}
