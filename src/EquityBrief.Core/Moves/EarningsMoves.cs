using EquityBrief.Core.Bars;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Moves;

// A stock's typical earnings move: how many prints were read and how many were left out, the oldest read, and the
// median size of their reactions in per cent, none where none was read. Named for the earnings so it is not taken for
// the typical daily move every distance, stop and gate reads.
public sealed record EarningsMove(int Prints, int Unread, DateOnly? FirstPrint, double? MedianPct)
{
    // Stated only over a year of reports or more, since a median of fewer is a print or two read as a habit.
    public bool Stated => MedianPct is not null && Prints >= EarningsMoves.FewestPrints;
}

// The typical earnings move, worked over every print the store holds: the calendar's year read off the stored bars by
// the reaction record's own function, and the prints before the store's year read off the history pulled before it by
// the earnings rule's own function one print at a time, each standing only where the pull holds the session the rule
// took and the session before it with no session it misses between them, by the rule the pull reads its own holes by.
// The median of the sizes, a fall and a rise of one size alike, is the move the page draws each reaction against.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
public static class EarningsMoves
{
    // The fewest prints a typical earnings move is stated over: a year of quarterly reports.
    public const int FewestPrints = 4;

    // The median size of the reactions read, in per cent, with the prints read and left out and the oldest read. Two
    // prints the provider files on different days that the earnings rule reads on one session are one report, read once.
    public static EarningsMove Of(IReadOnlyList<(DateOnly ReportDate, DateOnly Session, double MovePct)> read, int unread)
    {
        (DateOnly ReportDate, DateOnly Session, double MovePct)[] once = [.. read.GroupBy(print => print.Session).Select(session => session.MinBy(print => print.ReportDate))];

        return new(
            once.Length,
            unread,
            once.Length == 0 ? null : once.Min(print => print.ReportDate),
            SwingReadings.Median([.. once.Select(print => Math.Abs(print.MovePct))]));
    }

    // Whether a print read off the pull stands: the session the earnings rule took for it is the first session on the
    // print's day, or after it for a report after the close, and the session before it in the pull is the session before
    // that one, so neither a session the pull is missing nor a stray day it holds lies inside the reaction or before it.
    public static bool Stands(DateOnly reportDate, EventTiming timing, DateOnly session, DateOnly before, PulledCalendar calendar) =>
        (timing == EventTiming.After ? calendar.After(reportDate) : calendar.OnOrAfter(reportDate)) == session
        && calendar.SessionBefore(session) == before;
}
