namespace EquityBrief.Core.Providers;

// When in the session a report lands, as the provider files it.
//
// It decides which session the print is priced on: a report before the open
// moves that day's bar and one after the close moves the next. The provider
// leaves it unstated for some rows and that is a third value rather than a
// default, for the reason a bar count sits beside an average.
public enum EventTiming
{
    Unstated,
    Before,
    After,
}

// One dated event the provider files for a name.
//
// `PeriodEnd` is the fiscal period the report covers, which is not the date it
// is reported on and is the field most easily mistaken for it: the capture at
// 4.3 carries reports dated in October for periods ending in September.
//
// `Estimate` is kept as the provider sent it and nothing computes with it. It
// is here because the report section states what was expected against what
// arrived, and because a row carrying an estimate and no actual is a print that
// has not happened yet, which is the only reading this system takes from it.
public sealed record CalendarEvent(
    string Ticker,
    DateOnly EventDate,
    EventTiming Timing,
    DateOnly? PeriodEnd,
    string? Estimate,
    string? Actual);

// The index's dated events over a window, in one request.
//
// Nightly and once, and one request whatever the universe size, which is what
// keeps the earnings date off the per-name path: the ladder builder and the
// shortlist builder both need it every night, and the only other component that
// could fetch it runs on demand in phase 6.
// see: A calendar event is fetched once for the whole index, and the calendar holds provider events only
// see: The nightly run is arithmetic only
//
// Asked for by date range and never by ticker. The provider will filter by
// symbol, and a request carrying five hundred symbols is a URL that grows with
// the index, which is the page-count defect wearing a different hat: the count
// stays at one and the request stops being a constant.
// see: A feed that pages counts every page, and a page count that grows with the index is a per-name call
public interface IEarningsCalendarFeed
{
    Task<IReadOnlyList<CalendarEvent>> EventsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default);

    int Requests { get; }
}
