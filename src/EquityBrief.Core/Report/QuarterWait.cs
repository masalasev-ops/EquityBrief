using System.Globalization;

namespace EquityBrief.Core.Report;

// One ask the quarters fetch made of the provider for a member: the night it asked on, what came of it, how many nights it
// has asked for the quarter awaited, and the night it asks again where it is waiting a week.
public sealed record QuarterAskRow(DateOnly Session, string Outcome, int Nights, DateOnly? NextAsk);

// The first day the archive's facts carry a period's end for a company, and the form that carried it.
public sealed record FirstFiled(DateOnly PeriodEnd, DateOnly Filed, string Form);

// A quarter a results release reports that neither the provider's quarters nor a stored filing carry yet: the day the
// release was accepted, the quarter it reports, the night the quarters fetch next asks the provider for it and why, the day
// its filing is expected with how that was read, the day the filing is due, and the first of the two days, on which the
// page is expected to read the quarter.
public sealed record QuarterWait(DateOnly Released, DateOnly Quarter, DateOnly? NextAsk, string Ask, DateOnly? FilingExpected, string Filing, DateOnly FilingDue, DateOnly Clears);

// When a page whose newest results release reports a quarter newer than the one it draws is expected to draw it: on the
// first night the quarters fetch asks the provider for it, by the schedule the fetch keeps, or on the day its filing is
// expected, the quarter's end plus the days the company's filing of the same quarter took a year before and never after
// the day the filing is due, whichever comes first.
// see: A page drawing an older quarter than its newest results release reports names the quarter it awaits and the first day it is expected to read it
public static class QuarterWaits
{
    // The quarters fetch's schedule: each of these nights after its first ask, then a week after the last.
    public const int RetryNights = 5;

    public const int WeeklyRetryDays = 7;

    // The days after a quarter's end a large filer's quarterly report is due, and its annual report.
    public const int QuarterDueDays = 40;

    public const int YearDueDays = 60;

    // How far a year-earlier period's end may sit from the day a year before the quarter's, a fiscal calendar's weeks.
    const int NearDays = 7;

    public const string Stored = "stored";

    public static QuarterWait Of(DateOnly released, DateOnly drawnQuarter, DateOnly night, DateOnly? reportDate, IReadOnlyList<QuarterAskRow> asks, IReadOnlyList<FirstFiled> filed)
    {
        var next = drawnQuarter.AddMonths(3);
        var quarter = new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month));
        // The day the quarter is reported is the calendar's report date nearest on or after the release, which a release the
        // company filed ahead of its results precedes; the release's own day where the calendar holds none.
        var reported = reportDate is { } on && on >= released.AddDays(-NearDays) ? on : released;
        var ahead = reported > released.AddDays(NearDays);
        var since = asks.Where(ask => ask.Session > reported).OrderBy(ask => ask.Session).ToArray();
        DateOnly? nextAsk;
        string ask;

        if (since.Length == 0)
        {
            nextAsk = Weekday(night > reported ? night : reported);
            ask = ahead
                ? Invariant($"the calendar reports the quarter on {reported:d MMM yyyy}, after this release, and the quarters fetch first asks the provider for it on the night of {nextAsk:d MMM yyyy}")
                : Invariant($"the quarters fetch first asks the provider for it on the night of {nextAsk:d MMM yyyy}, the first night after its report");
        }
        else if (since[^1] is { Outcome: Stored } stored)
        {
            nextAsk = null;
            ask = Invariant($"the provider's quarters were stored on the night of {stored.Session:d MMM yyyy}");
        }
        else if (since[^1].Nights <= RetryNights)
        {
            var last = since[^1];

            nextAsk = Weekday(night > last.Session ? night : last.Session);
            ask = Invariant($"the provider had not posted it on the {Ordinal(last.Nights)} night asked, {last.Session:d MMM yyyy}, and is asked again on the night of {nextAsk:d MMM yyyy}");
        }
        else
        {
            var last = since[^1];

            nextAsk = last.NextAsk ?? last.Session.AddDays(WeeklyRetryDays);
            ask = Invariant($"the provider had not posted it after {last.Nights} nights asked, so it is asked weekly, next on the night of {nextAsk:d MMM yyyy}");
        }

        var yearBefore = quarter.AddYears(-1);
        var earlier = filed
            .Where(one => Math.Abs(one.PeriodEnd.DayNumber - yearBefore.DayNumber) <= NearDays && one.Filed > one.PeriodEnd)
            .OrderBy(one => one.Filed)
            .FirstOrDefault();
        var due = quarter.AddDays(earlier?.Form == "10-K" ? YearDueDays : QuarterDueDays);
        DateOnly? expected = null;
        string filing;

        if (earlier is { } then)
        {
            var lag = then.Filed.DayNumber - then.PeriodEnd.DayNumber;
            var guess = quarter.AddDays(lag);

            expected = guess < reported ? reported : guess > due ? due : guess;
            filing = expected > night
                ? Invariant($"its filing is expected about {expected:d MMM yyyy}, the quarter's end plus the {lag} days its {then.Form} for the quarter to {then.PeriodEnd:d MMM yyyy} took, and is due by {due:d MMM yyyy}")
                : Invariant($"its filing was expected about {expected:d MMM yyyy}, the quarter's end plus the {lag} days its {then.Form} for the quarter to {then.PeriodEnd:d MMM yyyy} took, and is not stored yet, and it is due by {due:d MMM yyyy}");
        }
        else
        {
            filing = Invariant($"no filing of the same quarter a year before is stored to read its timing from, and its filing is due by {due:d MMM yyyy}");
        }

        // A filing expected on or before the night and not stored yet is next read the night after it.
        var filingClears = expected ?? due;
        var clears = new[] { nextAsk, filingClears > night ? filingClears : Weekday(night) }.OfType<DateOnly>().Min();

        return new QuarterWait(released, quarter, nextAsk, ask, expected, filing, due, clears);
    }

    // The first weekday after a day, the night a fetch asks on next.
    static DateOnly Weekday(DateOnly after)
    {
        var day = after.AddDays(1);

        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    static string Ordinal(int count) => count switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        _ => count.ToString(CultureInfo.InvariantCulture) + "th",
    };

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
