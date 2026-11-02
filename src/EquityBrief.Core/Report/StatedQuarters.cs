using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Report;

// The quarter ends the company's own filings state. The provider labels a quarter with the last day of a month and a
// company ending its quarters on a weekday ends them up to a week from it, so a page draws the filing's date and goes on
// matching quarters on the label: a quarter no filing states within the week, one still to report among them, keeps it.
// see: A page draws a quarter's end as the company's filing states it, and matches quarters on the provider's label
public sealed class StatedQuarters(IEnumerable<DateOnly> stated)
{
    readonly DateOnly[] ends = [.. stated.Distinct()];

    public DateOnly Of(DateOnly label) => ends
        .Where(end => Math.Abs(end.DayNumber - label.DayNumber) <= QuarterFetch.NearDays)
        .OrderBy(end => Math.Abs(end.DayNumber - label.DayNumber))
        .Select(end => (DateOnly?)end)
        .FirstOrDefault() ?? label;

    public DateOnly? Of(DateOnly? label) => label is { } day ? Of(day) : null;
}
