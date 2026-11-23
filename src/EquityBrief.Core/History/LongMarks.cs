using EquityBrief.Core.Chart;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// What the long history's reader does with a history-ends mark. A mark ends only the member span it falls in: the bars on
// and before it belong to the spans that joined on or before it, and the bars after it to the spans that joined after it,
// which read their own bars alone and never look back across it. A mark falling inside a member span with the stock trading
// on after it, or with spans of one index on both sides of it, is settled by evidence: read through as misdated where the
// code's filer files its reports on both sides and the price carries on, kept as a reused code where the filer stops and
// the price gaps or jumps, and put to the operator on the marking page where the two disagree or the filer is not on file.
// A mark the operator has ruled on reads as they ruled.
// see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
public static class LongMarks
{
    public const string ReadThrough = "read through";

    public const string Kept = "kept";

    public const string ToOperator = "to the operator";

    public const string SameFiler = "files on both sides";

    public const string FilerStops = "stops filing";

    public const string NotOnFile = "not on file";

    public const string Continues = "carries on";

    public const string Gap = "gaps";

    public const string Jump = "jumps";

    public const string TooFew = "too few sessions";

    // A mark is odd where more traded sessions than this follow it inside a span it falls in.
    public const int TradedAfterMost = 5;

    // The filer files on both sides where it filed a quarterly or annual report within this many days before the mark and
    // another more than this many days after it, the reports a company files about its own closing quarter left out.
    public const int FiledBeforeDays = 200;

    public const int FiledAfterDays = 45;

    // The price gaps where more sessions than this pass between the mark's session and the next it trades, and jumps where
    // its move to that session is more than this many times its median daily move over the sessions before, the median
    // never read under a quarter of a percent and read only over enough sessions.
    public const int GapSessions = 5;

    public const double JumpTimes = 4;

    public const double MoveFloor = 0.0025;

    public const int MoveSessions = 63;

    public const int FewestMoves = 20;

    public static IReadOnlyList<string> PeriodicForms { get; } = ["10-Q", "10-K", "10-K405", "10-QSB", "10-KSB", "10-KT", "10-Q/A", "10-K/A"];

    // Whether a mark is odd: inside a span with more traded sessions after it than the most before that span's end, or with
    // spans of one index on both sides of it.
    public static bool Odd(DateOnly mark, IReadOnlyList<(string Index, DateOnly Joined, DateOnly? Left)> spans, IReadOnlyList<DateOnly> traded)
    {
        foreach (var span in spans.Where(span => span.Joined <= mark && (span.Left is not { } left || left > mark)))
        {
            if (traded.Count(day => day > mark && (span.Left is not { } end || day < end)) > TradedAfterMost)
            {
                return true;
            }
        }

        return spans.GroupBy(span => span.Index, StringComparer.Ordinal)
            .Any(index => index.Any(span => span.Joined <= mark) && index.Any(span => span.Joined > mark));
    }

    // What the code's filer did around the mark, read off its quarterly and annual reports.
    public static string FilerOf(DateOnly mark, IEnumerable<(DateOnly Filed, string Form)> filings)
    {
        var reports = filings.Where(filing => PeriodicForms.Contains(filing.Form, StringComparer.Ordinal)).Select(filing => filing.Filed).ToArray();
        var before = reports.Any(filed => filed <= mark && mark.DayNumber - filed.DayNumber <= FiledBeforeDays);
        var after = reports.Any(filed => filed.DayNumber - mark.DayNumber > FiledAfterDays);

        return before && after ? SameFiler : before ? FilerStops : NotOnFile;
    }

    // What the price did from the mark's session to the next it traded, over the sessions it traded.
    public static string PriceOf(IReadOnlyList<SweepBar> traded, DateOnly mark, IReadOnlyList<DateOnly> calendar)
    {
        var at = -1;

        for (var bar = 0; bar < traded.Count && traded[bar].Session <= mark; bar++)
        {
            at = bar;
        }

        if (at < 0 || at + 1 >= traded.Count || !(traded[at].Close > 0))
        {
            return TooFew;
        }

        var next = traded[at + 1];
        var skipped = calendar.Count(session => session > traded[at].Session && session < next.Session);

        if (skipped > GapSessions)
        {
            return Gap;
        }

        var moves = new List<double>();

        for (var bar = Math.Max(1, at - MoveSessions + 1); bar <= at; bar++)
        {
            if (traded[bar - 1].Close > 0)
            {
                moves.Add(Math.Abs(Statistic.FromPrice(traded[bar].Close) / Statistic.FromPrice(traded[bar - 1].Close) - 1));
            }
        }

        if (moves.Count < FewestMoves)
        {
            return TooFew;
        }

        moves.Sort();

        var median = moves.Count % 2 == 1 ? moves[moves.Count / 2] : (moves[(moves.Count / 2) - 1] + moves[moves.Count / 2]) / 2;
        var move = Math.Abs(Statistic.FromPrice(next.Close) / Statistic.FromPrice(traded[at].Close) - 1);

        return move > JumpTimes * Math.Max(median, MoveFloor) ? Jump : Continues;
    }

    // The two tests together: read through where both say one company carries on, kept where both say another took the code,
    // and to the operator otherwise.
    public static string Settle(string filer, string price) =>
        filer == SameFiler && price == Continues ? ReadThrough
        : filer == FilerStops && price is Gap or Jump ? Kept
        : ToOperator;

    // A code's series cut into the companies its history-ends mark parts, each with the spans it reads: the sessions before a
    // start left out and a bad print skipped as the long reader reads them; where the mark is read through, one series; and
    // otherwise the bars on and before the mark with the spans that joined on or before it, then the bars after it with the
    // spans that joined after it, a part holding no span left out.
    public static IReadOnlyList<(SweepBar[] Bars, (DateOnly? Joined, DateOnly? Left)[] Spans)> Parts(
        IReadOnlyList<SweepBar> bars,
        IReadOnlyList<SessionMark> marks,
        IReadOnlyList<(DateOnly? Joined, DateOnly? Left)> spans,
        bool readThrough)
    {
        var starts = marks.Where(mark => mark.Kind == SessionMarking.StartsHere).Select(mark => (DateOnly?)mark.Session).Max();
        var bad = marks.Where(mark => mark.Kind == SessionMarking.BadPrint).Select(mark => mark.Session).ToHashSet();
        var kept = bars.Where(bar => (starts is not { } start || bar.Session >= start) && !bad.Contains(bar.Session)).ToArray();
        var ends = readThrough ? null : marks.Where(mark => mark.Kind == SessionMarking.HistoryEnds).Select(mark => (DateOnly?)mark.Session).Min();

        if (ends is not { } end)
        {
            return [(kept, [.. spans])];
        }

        var parts = new List<(SweepBar[] Bars, (DateOnly? Joined, DateOnly? Left)[] Spans)>();
        (DateOnly? Joined, DateOnly? Left)[] earlier = [.. spans.Where(span => span.Joined is not { } joined || joined <= end)];
        (DateOnly? Joined, DateOnly? Left)[] later = [.. spans.Where(span => span.Joined is { } joined && joined > end)];

        if (earlier.Length > 0 || later.Length == 0)
        {
            parts.Add(([.. kept.Where(bar => bar.Session <= end)], earlier));
        }

        if (later.Length > 0)
        {
            parts.Add(([.. kept.Where(bar => bar.Session > end)], later));
        }

        return parts;
    }
}
