using System.Globalization;

namespace EquityBrief.Core.Cards;

// Where a company's reports stand on a night: the quarter it reported and the day it reported it, and the day of its next
// report where one is dated. A stored quarter's report day is what the company did, and the calendar what is dated; the
// provider's expected day, written before the report, is read only where neither says.
// see: A quarter reported before its filing is stored is labelled as the release's, its reported day and next report read from its stored row and then the calendar and its release quoted where it says what it reported
public sealed record ReportedDay(DateOnly? Quarter, DateOnly? On, DateOnly? NextOn)
{
    public static ReportedDay Of(
        string? expectedQuarter,
        string? expectedOn,
        DateOnly? night,
        IEnumerable<(DateOnly PeriodEnd, DateOnly? ReportDate)> stored,
        IEnumerable<DateOnly> calendar)
    {
        var quarter = Day(expectedQuarter);
        var expected = Day(expectedOn);

        if (night is not { } on)
        {
            return new ReportedDay(null, null, expected);
        }

        DateOnly[] dated = [.. calendar];
        DateOnly? next = dated.Where(day => day > on).Select(day => (DateOnly?)day).Min();

        if (quarter is not { } awaited)
        {
            return new ReportedDay(null, null, next ?? (expected > on ? expected : null));
        }

        if (stored
            .Where(row => row.PeriodEnd >= awaited && row.ReportDate <= on)
            .OrderByDescending(row => row.PeriodEnd)
            .Select(row => ((DateOnly PeriodEnd, DateOnly? ReportDate)?)row)
            .FirstOrDefault() is { } reported)
        {
            return new ReportedDay(reported.PeriodEnd, reported.ReportDate, next);
        }

        if (dated.Where(day => day > awaited && day <= on).Select(day => (DateOnly?)day).Max() is { } datedOn)
        {
            return new ReportedDay(awaited, datedOn, next);
        }

        return next is not null || expected > on
            ? new ReportedDay(null, null, next ?? expected)
            : expected is { } passed
                ? new ReportedDay(awaited, passed, null)
                : new ReportedDay(null, null, null);
    }

    static DateOnly? Day(string? stored) =>
        DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
